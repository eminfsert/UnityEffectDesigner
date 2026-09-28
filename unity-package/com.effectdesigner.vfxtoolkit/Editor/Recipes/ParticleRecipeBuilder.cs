using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EffectDesigner.VFXToolkit.Editor.Capture;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor.Recipes
{
    public sealed class SystemReport
    {
        public string name;
        public string path;
        public bool created;
        public List<string> applied = new List<string>();
    }

    public sealed class ParticleRecipeResult
    {
        public string toolkitVersion = ToolkitInfo.Version;
        public string root;
        public string prefab;
        public bool dryRun;
        public List<SystemReport> systems = new List<SystemReport>();
        public List<string> warnings = new List<string>();
        /// <summary>Problems hit while applying (after validation passed); the other changes were kept.</summary>
        public List<string> errors = new List<string>();
    }

    /// <summary>
    /// Builds or updates a hierarchy of Shuriken particle systems from one JSON recipe.
    ///
    /// The whole recipe is compiled and validated first; if any key or value is wrong,
    /// nothing in the project changes and every problem is reported at once. Then the
    /// systems are created/updated in hierarchy order, modules are applied, sub-emitters
    /// are linked, and the result is optionally saved as a prefab.
    ///
    /// Updates are patches: only the keys present in the recipe change. Use
    /// "reset": true on a system to rebuild it from Unity defaults.
    /// </summary>
    public static class ParticleRecipeBuilder
    {
        const string UrpParticleMaterial = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";

        static readonly HashSet<string> SystemKeys = new HashSet<string>(new[]
        {
            "name", "parent", "position", "rotation", "scale", "reset", "active", "renderer", "order",
        }.Select(RecipeValues.Normalize));

        static readonly Dictionary<string, string> RendererAliases = new Dictionary<string, string>
        {
            { "material", "sharedmaterial" },
        };

        /// <summary>Names agents commonly reach for that differ from Unity's API names (normalized).</summary>
        static readonly Dictionary<string, Dictionary<string, string>> ModuleAliases = new Dictionary<string, Dictionary<string, string>>
        {
            { "main", new Dictionary<string, string> { { "looping", "loop" } } },
        };

        sealed class ModulePlan
        {
            public string Name;
            public PropertyInfo Module;
            public bool AutoEnable;
            public List<Action<object>> Setters;
        }

        sealed class SystemPlan
        {
            public string Name;
            public string Parent;
            public JObject Json;
            public Vector3? Position, Rotation, Scale;
            public bool Reset;
            public bool? Active;
            /// <summary>Sibling index under the parent (draw/inspection order); new systems are appended otherwise.</summary>
            public int? Order;
            public readonly List<ModulePlan> Modules = new List<ModulePlan>();
            public List<Action<object>> Renderer = new List<Action<object>>();
            public bool RendererHasMaterial;
            public ParticleSystem.Burst[] Bursts;
            public List<ParticleSystemVertexStream> VertexStreams, TrailVertexStreams;
            public List<Sprite> Sprites;
            public List<(ParticleSystemCustomData stream, JObject json)> CustomData;
            public List<(string system, ParticleSystemSubEmitterType type, ParticleSystemSubEmitterProperties inherit, float probability)> SubEmitters;
        }

        /// <summary>A validated recipe, ready to apply.</summary>
        sealed class CompiledRecipe
        {
            public RecipeContext Context;
            public List<SystemPlan> Systems;
            public bool DryRun, Overwrite;
            public string Target, SavePrefab, RootName;
        }

        /// <param name="errors">Validation errors. When any are returned, nothing in the project was changed.</param>
        public static ParticleRecipeResult Apply(JObject recipe, out List<string> errors)
        {
            var compiled = Compile(recipe, out errors);
            if (compiled == null)
                return null;

            if (compiled.DryRun)
            {
                var report = new ParticleRecipeResult { dryRun = true };
                foreach (var plan in compiled.Systems)
                    report.systems.Add(new SystemReport { name = plan.Name, applied = Summarize(plan) });
                report.warnings.AddRange(compiled.Context.Warnings);
                return report;
            }
            return Execute(compiled, errors);
        }

        /// <summary>
        /// Parses and validates the whole recipe without touching the project. Kept free of
        /// scene/asset calls (other than asset lookups for references) so it stays testable.
        /// </summary>
        static CompiledRecipe Compile(JObject recipe, out List<string> errors)
        {
            errors = new List<string>();
            var ctx = new RecipeContext();

            // Some MCP clients send nested arrays/objects as JSON strings.
            foreach (var key in new[] { "systems", "palette" })
            {
                if (recipe[key]?.Type == JTokenType.String)
                {
                    try { recipe[key] = JToken.Parse(recipe[key].Value<string>()); }
                    catch (Newtonsoft.Json.JsonException ex) { errors.Add($"'{key}' is a string but not valid JSON: {ex.Message}"); }
                }
            }

            if (recipe["palette"] is JObject palette)
            {
                foreach (var p in palette.Properties())
                {
                    try { ctx.Palette[p.Name] = RecipeValues.ToColor(p.Value, ctx, $"palette.{p.Name}"); }
                    catch (RecipeException ex) { errors.Add(ex.Message); }
                }
            }

            if (!(recipe["systems"] is JArray systems) || systems.Count == 0)
            {
                errors.Add("'systems' must be a non-empty array of particle system definitions.");
                return null;
            }

            foreach (var s in systems.OfType<JObject>())
            {
                string name = s["name"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(name))
                    errors.Add("Every system needs a 'name'.");
                else if (!ctx.SystemNames.Add(name))
                    errors.Add($"System name '{name}' is used twice.");
            }
            if (errors.Count > 0)
                return null;

            var plans = new List<SystemPlan>();
            foreach (var s in systems.OfType<JObject>())
                plans.Add(CompilePlan(s, ctx, errors));
            var ordered = OrderByParent(plans, errors);
            if (errors.Count > 0)
                return null;

            return new CompiledRecipe
            {
                Context = ctx,
                Systems = ordered,
                DryRun = recipe["dry_run"]?.Value<bool>() ?? false,
                Overwrite = recipe["overwrite"]?.Value<bool>() ?? false,
                Target = recipe["target"]?.Value<string>(),
                SavePrefab = recipe["save_prefab"]?.Value<string>(),
                RootName = recipe["name"]?.Value<string>(),
            };
        }

        static ParticleRecipeResult Execute(CompiledRecipe compiled, List<string> errors)
        {
            var ctx = compiled.Context;
            var ordered = compiled.Systems;
            string target = compiled.Target;
            string savePrefab = compiled.SavePrefab;
            string rootName = compiled.RootName;
            var result = new ParticleRecipeResult();

            bool editingPrefabAsset = !string.IsNullOrEmpty(target) && target.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
            // A new effect saved as a prefab is built in a preview scene, so the open scene is never touched.
            bool isolatedNewPrefab = string.IsNullOrEmpty(target) && !string.IsNullOrEmpty(savePrefab);
            bool offScene = editingPrefabAsset || isolatedNewPrefab;
            var previewScene = default(UnityEngine.SceneManagement.Scene);
            GameObject root;
            if (isolatedNewPrefab && AssetDatabase.LoadAssetAtPath<GameObject>(savePrefab) != null && !compiled.Overwrite)
            {
                errors.Add($"A prefab already exists at '{savePrefab}'. Pass \"target\": \"{savePrefab}\" to patch it, or \"overwrite\": true to replace it.");
                return null;
            }
            if (editingPrefabAsset)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(target) == null)
                {
                    errors.Add($"No prefab at '{target}'. To create one, omit 'target' and pass 'save_prefab'.");
                    return null;
                }
                root = PrefabUtility.LoadPrefabContents(target);
            }
            else if (!string.IsNullOrEmpty(target))
            {
                root = TimelineCapture.ResolveTarget(target, out string resolveError);
                if (root == null)
                {
                    errors.Add(resolveError);
                    return null;
                }
            }
            else if (isolatedNewPrefab)
            {
                previewScene = EditorSceneManager.NewPreviewScene();
                root = new GameObject(string.IsNullOrWhiteSpace(rootName) ? Path.GetFileNameWithoutExtension(savePrefab) : rootName);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, previewScene);
            }
            else
            {
                // No target and no save_prefab: an explicit request to build in the open scene.
                root = new GameObject(string.IsNullOrWhiteSpace(rootName) ? "VFX_New" : rootName);
                Undo.RegisterCreatedObjectUndo(root, "Create VFX from recipe");
            }

            try
            {
                foreach (var plan in ordered)
                    result.systems.Add(Materialize(plan, root, ctx, offScene));
                for (int i = 0; i < ordered.Count; i++)
                    ApplyPlan(ordered[i], ctx, offScene, result.systems[i], result.errors);
                foreach (var plan in ordered)
                    LinkSubEmitters(plan, ctx, result.errors);

                result.root = editingPrefabAsset ? target : isolatedNewPrefab ? savePrefab : HierarchyPath(root.transform);
                if (editingPrefabAsset)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, target);
                    result.prefab = target;
                }
                else if (isolatedNewPrefab)
                {
                    MaterialBuilder.EnsureFolder(Path.GetDirectoryName(savePrefab).Replace('\\', '/'));
                    PrefabUtility.SaveAsPrefabAsset(root, savePrefab);
                    result.prefab = savePrefab;
                }
                else
                {
                    if (root.scene.IsValid())
                        EditorSceneManager.MarkSceneDirty(root.scene);
                    if (!string.IsNullOrEmpty(savePrefab))
                    {
                        MaterialBuilder.EnsureFolder(Path.GetDirectoryName(savePrefab).Replace('\\', '/'));
                        PrefabUtility.SaveAsPrefabAssetAndConnect(root, savePrefab, InteractionMode.UserAction);
                        result.prefab = savePrefab;
                    }
                }
            }
            finally
            {
                if (editingPrefabAsset)
                    PrefabUtility.UnloadPrefabContents(root);
                if (isolatedNewPrefab)
                    EditorSceneManager.ClosePreviewScene(previewScene);
            }

            result.warnings.AddRange(ctx.Warnings);
            return result;
        }

        // ---------------------------------------------------------------- compile

        static SystemPlan CompilePlan(JObject json, RecipeContext ctx, List<string> errors)
        {
            string name = json["name"].Value<string>();
            string at = $"systems[{name}]";
            var plan = new SystemPlan { Name = name, Json = json, Parent = json["parent"]?.Value<string>() };

            TryVector(json, "position", at, ctx, errors, v => plan.Position = v);
            TryVector(json, "rotation", at, ctx, errors, v => plan.Rotation = v);
            TryVector(json, "scale", at, ctx, errors, v => plan.Scale = v);
            plan.Reset = json["reset"]?.Value<bool>() ?? false;
            plan.Active = json["active"]?.Value<bool>();
            plan.Order = json["order"]?.Value<int>();
            if (plan.Parent != null && !ctx.SystemNames.Contains(plan.Parent))
                errors.Add($"{at}.parent: '{plan.Parent}' is not a system in this recipe.");

            foreach (var entry in json.Properties())
            {
                string key = RecipeValues.Normalize(entry.Name);
                if (SystemKeys.Contains(key))
                    continue;
                if (!ModuleBinder.Modules.TryGetValue(key, out var module))
                {
                    var known = ModuleBinder.Modules.Values.Select(m => m.Name).Concat(new[] { "name", "parent", "position", "rotation", "scale", "reset", "active", "renderer", "order" });
                    errors.Add($"{at}.{entry.Name}: unknown module. Did you mean: {string.Join(", ", ModuleBinder.Suggest(key, known))}?");
                    continue;
                }

                string where = $"{at}.{entry.Name}";
                switch (key)
                {
                    case "subemitters":
                        plan.SubEmitters = CompileSubEmitters(entry.Value, where, ctx, errors);
                        continue;
                    case "customdata":
                        plan.CustomData = CompileCustomData(entry.Value, where, errors);
                        continue;
                }

                if (!(entry.Value is JObject moduleJson))
                {
                    errors.Add($"{where}: expected an object of module properties.");
                    continue;
                }

                var skip = new List<string>();
                if (key == "emission" && moduleJson["bursts"] != null)
                {
                    skip.Add("bursts");
                    plan.Bursts = CompileBursts(moduleJson["bursts"], where + ".bursts", errors);
                }
                if (key == "texturesheetanimation" && moduleJson["sprites"] != null)
                {
                    skip.Add("sprites");
                    plan.Sprites = CompileSprites(moduleJson["sprites"], where + ".sprites", ctx, errors);
                }

                plan.Modules.Add(new ModulePlan
                {
                    Name = module.Name,
                    Module = module,
                    AutoEnable = moduleJson["enabled"] == null && ModuleBinder.HasProperty(module.PropertyType, "enabled"),
                    Setters = ModuleBinder.Compile(module.PropertyType, moduleJson, where, ctx, skip, errors,
                        ModuleAliases.TryGetValue(key, out var aliases) ? aliases : null),
                });
            }

            if (json["renderer"] is JObject rendererJson)
            {
                var skip = new List<string> { "vertexstreams", "trailvertexstreams" };
                plan.Renderer = ModuleBinder.Compile(typeof(ParticleSystemRenderer), rendererJson, $"{at}.renderer", ctx, skip, errors, RendererAliases);
                plan.RendererHasMaterial = rendererJson["material"] != null || rendererJson["shared_material"] != null || rendererJson["sharedMaterial"] != null;
                plan.VertexStreams = CompileStreams(rendererJson["vertex_streams"] ?? rendererJson["vertexStreams"], $"{at}.renderer.vertex_streams", errors);
                plan.TrailVertexStreams = CompileStreams(rendererJson["trail_vertex_streams"] ?? rendererJson["trailVertexStreams"], $"{at}.renderer.trail_vertex_streams", errors);
            }
            else if (json["renderer"] != null)
            {
                errors.Add($"{at}.renderer: expected an object.");
            }

            return plan;
        }

        static void TryVector(JObject json, string key, string at, RecipeContext ctx, List<string> errors, Action<Vector3> set)
        {
            if (json[key] == null) return;
            try { set((Vector3)RecipeValues.Convert(json[key], typeof(Vector3), ctx, $"{at}.{key}")); }
            catch (RecipeException ex) { errors.Add(ex.Message); }
        }

        static ParticleSystem.Burst[] CompileBursts(JToken token, string where, List<string> errors)
        {
            if (!(token is JArray arr))
            {
                errors.Add($"{where}: expected an array of {{\"time\", \"count\", \"cycles\", \"interval\", \"probability\"}}.");
                return null;
            }
            var bursts = new List<ParticleSystem.Burst>();
            for (int i = 0; i < arr.Count; i++)
            {
                string at = $"{where}[{i}]";
                if (!(arr[i] is JObject b) || b["count"] == null)
                {
                    errors.Add($"{at}: each burst needs at least \"count\" (and usually \"time\").");
                    continue;
                }
                try
                {
                    var burst = new ParticleSystem.Burst(b["time"]?.Value<float>() ?? 0f, RecipeValues.ToMinMaxCurve(b["count"], at + ".count"));
                    if (b["cycles"] != null) burst.cycleCount = b["cycles"].Value<int>();
                    if (b["interval"] != null) burst.repeatInterval = b["interval"].Value<float>();
                    if (b["probability"] != null) burst.probability = b["probability"].Value<float>();
                    bursts.Add(burst);
                }
                catch (RecipeException ex) { errors.Add(ex.Message); }
            }
            return bursts.ToArray();
        }

        static List<Sprite> CompileSprites(JToken token, string where, RecipeContext ctx, List<string> errors)
        {
            var sprites = new List<Sprite>();
            var items = token is JArray arr ? arr.ToList() : new List<JToken> { token };
            for (int i = 0; i < items.Count; i++)
            {
                try { sprites.Add((Sprite)RecipeValues.Convert(items[i], typeof(Sprite), ctx, $"{where}[{i}]")); }
                catch (RecipeException ex) { errors.Add(ex.Message); }
            }
            return sprites;
        }

        static List<ParticleSystemVertexStream> CompileStreams(JToken token, string where, List<string> errors)
        {
            if (token == null) return null;
            if (!(token is JArray arr))
            {
                errors.Add($"{where}: expected an array of stream names, e.g. [\"Position\", \"Color\", \"UV\", \"Custom1XY\"].");
                return null;
            }
            var streams = new List<ParticleSystemVertexStream>();
            foreach (var s in arr)
            {
                try { streams.Add((ParticleSystemVertexStream)RecipeValues.ToEnum(s, typeof(ParticleSystemVertexStream), where)); }
                catch (RecipeException ex) { errors.Add(ex.Message); }
            }
            return streams;
        }

        static List<(ParticleSystemCustomData, JObject)> CompileCustomData(JToken token, string where, List<string> errors)
        {
            var list = new List<(ParticleSystemCustomData, JObject)>();
            if (!(token is JObject obj))
            {
                errors.Add($"{where}: expected {{\"custom1\": {{\"mode\": \"vector\", \"components\": 2, \"x\": curve, ...}}, \"custom2\": ...}}.");
                return list;
            }
            foreach (var p in obj.Properties())
            {
                string key = RecipeValues.Normalize(p.Name);
                if (key == "enabled") continue;
                if (!(p.Value is JObject streamJson))
                {
                    errors.Add($"{where}.{p.Name}: expected an object.");
                    continue;
                }
                if (key == "custom1") list.Add((ParticleSystemCustomData.Custom1, streamJson));
                else if (key == "custom2") list.Add((ParticleSystemCustomData.Custom2, streamJson));
                else errors.Add($"{where}.{p.Name}: only custom1 and custom2 exist.");
            }
            return list;
        }

        static List<(string, ParticleSystemSubEmitterType, ParticleSystemSubEmitterProperties, float)> CompileSubEmitters(JToken token, string where, RecipeContext ctx, List<string> errors)
        {
            var list = new List<(string, ParticleSystemSubEmitterType, ParticleSystemSubEmitterProperties, float)>();
            if (!(token is JArray arr))
            {
                errors.Add($"{where}: expected an array of {{\"system\", \"type\", \"inherit\", \"probability\"}}.");
                return list;
            }
            for (int i = 0; i < arr.Count; i++)
            {
                string at = $"{where}[{i}]";
                if (!(arr[i] is JObject s) || s["system"] == null)
                {
                    errors.Add($"{at}: each sub-emitter needs \"system\" (the name of another system in this recipe).");
                    continue;
                }
                string system = s["system"].Value<string>();
                if (!ctx.SystemNames.Contains(system))
                {
                    errors.Add($"{at}.system: '{system}' is not a system in this recipe.");
                    continue;
                }
                try
                {
                    var type = s["type"] != null
                        ? (ParticleSystemSubEmitterType)RecipeValues.ToEnum(s["type"], typeof(ParticleSystemSubEmitterType), at + ".type")
                        : ParticleSystemSubEmitterType.Birth;
                    var inherit = ParticleSystemSubEmitterProperties.InheritNothing;
                    if (s["inherit"] != null)
                    {
                        var names = s["inherit"] is JArray ia ? ia.Select(t => t.Value<string>()) : new[] { s["inherit"].Value<string>() };
                        foreach (var n in names)
                        {
                            string withPrefix = RecipeValues.Normalize(n).StartsWith("inherit") ? n : "inherit_" + n;
                            inherit |= (ParticleSystemSubEmitterProperties)RecipeValues.ToEnum(withPrefix, typeof(ParticleSystemSubEmitterProperties), at + ".inherit");
                        }
                    }
                    list.Add((system, type, inherit, s["probability"]?.Value<float>() ?? 1f));
                }
                catch (RecipeException ex) { errors.Add(ex.Message); }
            }
            return list;
        }

        static List<SystemPlan> OrderByParent(List<SystemPlan> plans, List<string> errors)
        {
            var ordered = new List<SystemPlan>();
            var placed = new HashSet<string>();
            var pending = new List<SystemPlan>(plans);
            while (pending.Count > 0)
            {
                var ready = pending.Where(p => p.Parent == null || placed.Contains(p.Parent)).ToList();
                if (ready.Count == 0)
                {
                    errors.Add($"Parent cycle between systems: {string.Join(", ", pending.Select(p => p.Name))}.");
                    break;
                }
                foreach (var p in ready)
                {
                    ordered.Add(p);
                    placed.Add(p.Name);
                    pending.Remove(p);
                }
            }
            return ordered;
        }

        static List<string> Summarize(SystemPlan plan)
        {
            var list = new List<string>();
            if (plan.Reset) list.Add("reset");
            if (plan.Position.HasValue) list.Add("position");
            if (plan.Rotation.HasValue) list.Add("rotation");
            if (plan.Scale.HasValue) list.Add("scale");
            if (plan.Active.HasValue) list.Add("active");
            if (plan.Order.HasValue) list.Add("order");
            list.AddRange(plan.Modules.Select(m => $"{ModuleBinder.ToSnake(m.Name)}({m.Setters.Count})"));
            if (plan.Bursts != null) list.Add($"bursts({plan.Bursts.Length})");
            if (plan.Sprites != null) list.Add($"sprites({plan.Sprites.Count})");
            if (plan.CustomData != null) list.Add($"custom_data({plan.CustomData.Count})");
            if (plan.SubEmitters != null) list.Add($"sub_emitters({plan.SubEmitters.Count})");
            if (plan.Renderer.Count > 0) list.Add($"renderer({plan.Renderer.Count})");
            if (plan.VertexStreams != null) list.Add($"vertex_streams({plan.VertexStreams.Count})");
            if (plan.TrailVertexStreams != null) list.Add($"trail_vertex_streams({plan.TrailVertexStreams.Count})");
            return list;
        }

        // ---------------------------------------------------------------- apply

        static SystemReport Materialize(SystemPlan plan, GameObject root, RecipeContext ctx, bool prefabContents)
        {
            Transform parent = plan.Parent != null ? ctx.System(plan.Parent).transform : root.transform;
            GameObject go = plan.Parent == null && plan.Name == root.name ? root : FindChild(root.transform, plan.Name);
            bool created = false;

            if (go != null && plan.Reset && go != root)
            {
                if (prefabContents) UnityEngine.Object.DestroyImmediate(go);
                else Undo.DestroyObjectImmediate(go);
                go = null;
            }
            else if (go != null && plan.Reset && go.TryGetComponent<ParticleSystem>(out var rootSystem))
            {
                // The renderer depends on the system, so the system goes first.
                UnityEngine.Object.DestroyImmediate(rootSystem);
                if (go.TryGetComponent<ParticleSystemRenderer>(out var leftover))
                    UnityEngine.Object.DestroyImmediate(leftover);
            }

            if (go == null)
            {
                go = new GameObject(plan.Name);
                if (!prefabContents)
                    Undo.RegisterCreatedObjectUndo(go, "Create particle system from recipe");
                go.transform.SetParent(parent, false);
                created = true;
            }
            else if (go != root && go.transform.parent != parent)
            {
                if (prefabContents) go.transform.SetParent(parent, false);
                else Undo.SetTransformParent(go.transform, parent, "Reparent particle system");
            }

            if (!go.TryGetComponent<ParticleSystem>(out _))
            {
                if (prefabContents) go.AddComponent<ParticleSystem>();
                else Undo.AddComponent<ParticleSystem>(go);
                created = true;
            }

            ctx.Systems[plan.Name] = go;
            return new SystemReport { name = plan.Name, path = HierarchyPath(go.transform), created = created };
        }

        static void ApplyPlan(SystemPlan plan, RecipeContext ctx, bool prefabContents, SystemReport report, List<string> errors)
        {
            var go = ctx.System(plan.Name);
            var ps = go.GetComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (!prefabContents)
                Undo.RecordObjects(new UnityEngine.Object[] { go.transform, go, ps, renderer }, "Apply particle recipe");

            // Several main-module properties (duration, looping...) can only change while stopped.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (plan.Position.HasValue) go.transform.localPosition = plan.Position.Value;
            if (plan.Rotation.HasValue) go.transform.localEulerAngles = plan.Rotation.Value;
            if (plan.Scale.HasValue) go.transform.localScale = plan.Scale.Value;
            if (plan.Active.HasValue) go.SetActive(plan.Active.Value);
            if (plan.Order.HasValue && go.transform.parent != null)
                go.transform.SetSiblingIndex(Mathf.Clamp(plan.Order.Value, 0, go.transform.parent.childCount - 1));

            foreach (var m in plan.Modules)
            {
                object module = m.Module.GetValue(ps);
                if (m.AutoEnable)
                    Run(() => m.Module.PropertyType.GetProperty("enabled").SetValue(module, true), $"{plan.Name}.{m.Name}.enabled", errors);
                foreach (var set in m.Setters)
                    Run(() => set(module), $"{plan.Name}.{m.Name}", errors);
            }

            if (plan.Bursts != null)
            {
                var emission = ps.emission;
                emission.enabled = true;
                emission.SetBursts(plan.Bursts);
            }
            // Only when this recipe touches timing, so unrelated patches do not repeat the warning.
            if (plan.Bursts != null || plan.Modules.Any(m => m.Name == "main" || m.Name == "emission"))
                WarnAboutLateBursts(ps, plan.Name, ctx);

            if (plan.Sprites != null)
            {
                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true;
                tsa.mode = ParticleSystemAnimationMode.Sprites;
                while (tsa.spriteCount > 0) tsa.RemoveSprite(0);
                foreach (var sprite in plan.Sprites) tsa.AddSprite(sprite);
            }

            if (plan.CustomData != null)
                ApplyCustomData(ps, plan, ctx, errors);

            foreach (var set in plan.Renderer)
                Run(() => set(renderer), $"{plan.Name}.renderer", errors);
            if (plan.VertexStreams != null) renderer.SetActiveVertexStreams(plan.VertexStreams);
#if UNITY_2022_1_OR_NEWER
            if (plan.TrailVertexStreams != null) renderer.SetActiveTrailVertexStreams(plan.TrailVertexStreams);
#else
            if (plan.TrailVertexStreams != null) ctx.Warnings.Add($"{plan.Name}: trail_vertex_streams needs Unity 2022.1+; ignored.");
#endif

            if (renderer.sharedMaterial == null && !plan.RendererHasMaterial)
            {
                var fallback = AssetDatabase.LoadAssetAtPath<Material>(UrpParticleMaterial);
                if (fallback != null)
                {
                    renderer.sharedMaterial = fallback;
                    ctx.Warnings.Add($"{plan.Name}: no material given; assigned URP ParticlesUnlit as a placeholder.");
                }
                else
                {
                    ctx.Warnings.Add($"{plan.Name}: renderer has no material and the URP default particle material was not found; the system will render pink or not at all.");
                }
            }

            report.applied = Summarize(plan);
        }

        static void ApplyCustomData(ParticleSystem ps, SystemPlan plan, RecipeContext ctx, List<string> errors)
        {
            var customData = ps.customData;
            customData.enabled = true;
            foreach (var (stream, json) in plan.CustomData)
            {
                string at = $"{plan.Name}.custom_data.{stream.ToString().ToLowerInvariant()}";
                try
                {
                    string mode = json["mode"]?.Value<string>() ?? (json["color"] != null ? "color" : "vector");
                    if (RecipeValues.Normalize(mode) == "color")
                    {
                        customData.SetMode(stream, ParticleSystemCustomDataMode.Color);
                        if (json["color"] != null)
                            customData.SetColor(stream, RecipeValues.ToMinMaxGradient(json["color"], ctx, at + ".color"));
                        continue;
                    }

                    customData.SetMode(stream, ParticleSystemCustomDataMode.Vector);
                    string[] axes = { "x", "y", "z", "w" };
                    int count = json["components"]?.Value<int>() ?? axes.Count(a => json[a] != null);
                    customData.SetVectorComponentCount(stream, Mathf.Clamp(count, 1, 4));
                    for (int i = 0; i < axes.Length; i++)
                    {
                        if (json[axes[i]] != null)
                            customData.SetVector(stream, i, RecipeValues.ToMinMaxCurve(json[axes[i]], $"{at}.{axes[i]}"));
                    }
                }
                catch (RecipeException ex) { errors.Add(ex.Message); }
            }
        }

        /// <summary>
        /// A non-looping system stops emitting when its duration ends, so a burst scheduled in the
        /// last frame before the end (or after it) never fires. Seen in a real effect: duration 0.1
        /// with bursts at 0.08 and 0.095 emitted only the first.
        /// </summary>
        static void WarnAboutLateBursts(ParticleSystem ps, string name, RecipeContext ctx)
        {
            var main = ps.main;
            if (main.loop)
                return;
            var emission = ps.emission;
            var bursts = new ParticleSystem.Burst[emission.burstCount];
            emission.GetBursts(bursts);
            const float frame = 1f / 60f;
            foreach (var b in bursts)
            {
                if (b.time > main.duration - frame)
                    ctx.Warnings.Add($"{name}: burst at {RecipeValues.Format(b.time)} s is within the last frame of a non-looping {RecipeValues.Format(main.duration)} s system and may never fire. " +
                                     $"Make main.duration at least {RecipeValues.Format(b.time + 2 * frame)} or move the burst earlier.");
            }
        }

        static void LinkSubEmitters(SystemPlan plan, RecipeContext ctx, List<string> errors)
        {
            if (plan.SubEmitters == null) return;
            var ps = ctx.System(plan.Name).GetComponent<ParticleSystem>();
            var sub = ps.subEmitters;
            sub.enabled = plan.SubEmitters.Count > 0;
            while (sub.subEmittersCount > 0) sub.RemoveSubEmitter(0);

            foreach (var (system, type, inherit, probability) in plan.SubEmitters)
            {
                var child = ctx.System(system);
                var childPs = child.GetComponent<ParticleSystem>();
                sub.AddSubEmitter(childPs, type, inherit, probability);
                if (!child.transform.IsChildOf(ps.transform))
                    ctx.Warnings.Add($"{plan.Name}: sub-emitter '{system}' is not a child of it; set \"parent\": \"{plan.Name}\" to keep the hierarchy conventional.");
                var emission = childPs.emission;
                if (emission.rateOverTimeMultiplier > 0f && emission.burstCount == 0)
                    ctx.Warnings.Add($"{system}: used as a sub-emitter but also emits on its own (rate_over_time > 0); sub-emitters usually use bursts or rate 0.");
            }
        }

        static void Run(Action action, string where, List<string> errors)
        {
            try { action(); }
            catch (TargetInvocationException ex) { errors.Add($"{where}: {ex.InnerException?.Message ?? ex.Message}"); }
            catch (Exception ex) { errors.Add($"{where}: {ex.Message}"); }
        }

        static GameObject FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != root && t.name == name)
                    return t.gameObject;
            }
            return null;
        }

        static string HierarchyPath(Transform t)
        {
            string path = t.name;
            for (var p = t.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }
    }
}
