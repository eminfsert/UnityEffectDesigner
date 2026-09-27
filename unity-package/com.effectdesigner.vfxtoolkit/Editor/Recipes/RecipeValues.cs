using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor.Recipes
{
    /// <summary>A value that can only be resolved once the recipe's GameObjects exist (references to other systems).</summary>
    sealed class Deferred
    {
        public readonly Func<object> Resolve;
        public Deferred(Func<object> resolve) { Resolve = resolve; }
    }

    /// <summary>Shared state while compiling and applying one recipe.</summary>
    sealed class RecipeContext
    {
        public readonly Dictionary<string, Color> Palette = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> SystemNames = new HashSet<string>(StringComparer.Ordinal);
        public readonly Dictionary<string, GameObject> Systems = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        public readonly List<string> Warnings = new List<string>();

        public GameObject System(string name)
        {
            if (!Systems.TryGetValue(name, out var go))
                throw new InvalidOperationException($"System '{name}' was not created.");
            return go;
        }
    }

    /// <summary>
    /// Converts recipe JSON into Unity values. The notation is designed for agents:
    ///   curves:    1.5 | [min, max] | {"curve": [[t, v], ...]} | {"ease": "ease_out_expo", "from": 1, "to": 0}
    ///              | {"curve_min": ..., "curve_max": ...} ; optional "multiplier"
    ///   colors:    "#RRGGBB[AA]" | "$palette_name" | {"color": "#..", "intensity": 2} (HDR, in stops) | [r, g, b, a]
    ///   gradients: "#hex" | ["#a", "#b"] (random between two) | {"gradient": {...}} | {"gradient_min": .., "gradient_max": ..}
    ///              | {"random_color": {...}} ; gradient = {"colors": [[t, color], ...] or [color, ...], "alphas": [[t, a], ...], "mode": "blend|fixed"}
    ///   assets:    "Assets/Path/To/Asset.ext" (materials may also be inline objects, see MaterialBuilder)
    ///   enums:     case-insensitive name, snake_case allowed ("stretch", "local", "sphere")
    /// </summary>
    static class RecipeValues
    {
        public static object Convert(JToken token, Type type, RecipeContext ctx, string where)
        {
            if (type == typeof(float)) return token.Value<float>();
            if (type == typeof(double)) return token.Value<double>();
            if (type == typeof(int)) return token.Value<int>();
            if (type == typeof(uint)) return token.Value<uint>();
            if (type == typeof(bool)) return token.Value<bool>();
            if (type == typeof(string)) return token.Value<string>();
            if (type.IsEnum) return ToEnum(token, type, where);
            if (type == typeof(Vector2)) { var a = Floats(token, 2, where); return new Vector2(a[0], a[1]); }
            if (type == typeof(Vector3)) { var a = Floats(token, 3, where); return new Vector3(a[0], a[1], a[2]); }
            if (type == typeof(Vector4)) { var a = Floats(token, 4, where); return new Vector4(a[0], a[1], a[2], a[3]); }
            if (type == typeof(Color)) return ToColor(token, ctx, where);
            if (type == typeof(ParticleSystem.MinMaxCurve)) return ToMinMaxCurve(token, where);
            if (type == typeof(ParticleSystem.MinMaxGradient)) return ToMinMaxGradient(token, ctx, where);
            if (type == typeof(AnimationCurve)) return ToAnimationCurve(token, where);
            if (type == typeof(Gradient)) return ToGradient(token, ctx, where);
            if (type == typeof(LayerMask)) return ToLayerMask(token, where);
            // Inline material definition: validated now, created/updated when the recipe is applied.
            // Kept out of ToObjectReference, which calls into the engine and cannot run in offline tests.
            if (type == typeof(Material) && token is JObject materialRecipe)
            {
                MaterialBuilder.Validate(materialRecipe, where);
                return new Deferred(() => MaterialBuilder.Build(materialRecipe, ctx, where));
            }
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return ToObjectReference(token, type, ctx, where);
            throw new RecipeException($"{where}: properties of type {type.Name} are not supported by recipes.");
        }

        static float[] Floats(JToken token, int count, string where)
        {
            if (token is JArray arr && arr.Count == count)
                return arr.Select(t => t.Value<float>()).ToArray();
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return Enumerable.Repeat(token.Value<float>(), count).ToArray();
            throw new RecipeException($"{where}: expected an array of {count} numbers.");
        }

        public static object ToEnum(JToken token, Type type, string where)
        {
            if (token.Type == JTokenType.Integer)
                return Enum.ToObject(type, token.Value<int>());
            string raw = token.Value<string>() ?? "";
            string wanted = Normalize(raw);
            foreach (var name in Enum.GetNames(type))
            {
                if (Normalize(name) == wanted)
                    return Enum.Parse(type, name);
            }
            throw new RecipeException($"{where}: '{raw}' is not a valid {type.Name}. Valid: {string.Join(", ", Enum.GetNames(type))}.");
        }

        /// <summary>Lowercase with underscores/dashes/spaces removed, so start_lifetime == startLifetime.</summary>
        public static string Normalize(string s) => new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        // ---------------- colors ----------------

        public static Color ToColor(JToken token, RecipeContext ctx, string where)
        {
            switch (token.Type)
            {
                case JTokenType.String:
                    return ParseColorString(token.Value<string>(), ctx, where);
                case JTokenType.Array:
                    var a = token.Select(t => t.Value<float>()).ToArray();
                    if (a.Length == 3) return new Color(a[0], a[1], a[2], 1f);
                    if (a.Length == 4) return new Color(a[0], a[1], a[2], a[3]);
                    break;
                case JTokenType.Object:
                    var obj = (JObject)token;
                    var baseToken = obj["color"] ?? obj["hex"];
                    if (baseToken != null)
                    {
                        var c = ToColor(baseToken, ctx, where);
                        float stops = obj["intensity"]?.Value<float>() ?? 0f;
                        float scale = Mathf.Pow(2f, stops);
                        var hdr = new Color(c.r * scale, c.g * scale, c.b * scale, c.a);
                        if (obj["alpha"] != null) hdr.a = obj["alpha"].Value<float>();
                        return hdr;
                    }
                    break;
            }
            throw new RecipeException($"{where}: expected a color (\"#RRGGBB\", \"$palette_name\", [r,g,b,a] or {{\"color\": .., \"intensity\": ..}}).");
        }

        static Color ParseColorString(string s, RecipeContext ctx, string where)
        {
            if (s.StartsWith("$"))
            {
                if (ctx.Palette.TryGetValue(s.Substring(1), out var p))
                    return p;
                throw new RecipeException($"{where}: palette color '{s}' is not defined. Palette: {string.Join(", ", ctx.Palette.Keys)}.");
            }
            if (ColorUtility.TryParseHtmlString(s, out var c))
                return c;
            throw new RecipeException($"{where}: '{s}' is not a color.");
        }

        // ---------------- curves ----------------

        public static ParticleSystem.MinMaxCurve ToMinMaxCurve(JToken token, string where)
        {
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return new ParticleSystem.MinMaxCurve(token.Value<float>());

            if (token is JArray arr)
            {
                if (arr.Count == 2 && arr.All(IsNumber))
                    return new ParticleSystem.MinMaxCurve(arr[0].Value<float>(), arr[1].Value<float>());
                // A bare key list is a curve.
                return new ParticleSystem.MinMaxCurve(1f, ToAnimationCurve(arr, where));
            }

            if (token is JObject obj)
            {
                float multiplier = obj["multiplier"]?.Value<float>() ?? 1f;
                if (obj["constant"] != null)
                    return new ParticleSystem.MinMaxCurve(obj["constant"].Value<float>());
                if (obj["random"] is JArray r && r.Count == 2)
                    return new ParticleSystem.MinMaxCurve(r[0].Value<float>(), r[1].Value<float>());
                if (obj["curve_min"] != null && obj["curve_max"] != null)
                    return new ParticleSystem.MinMaxCurve(multiplier, ToAnimationCurve(obj["curve_min"], where + ".curve_min"), ToAnimationCurve(obj["curve_max"], where + ".curve_max"));
                if (obj["curve"] != null)
                    return new ParticleSystem.MinMaxCurve(multiplier, ToAnimationCurve(obj["curve"], where + ".curve"));
                if (obj["ease"] != null)
                    return new ParticleSystem.MinMaxCurve(multiplier, ToAnimationCurve(obj, where));
            }
            throw new RecipeException($"{where}: expected a curve: number, [min, max], {{\"curve\": [[t, v], ...]}}, {{\"ease\": name, \"from\": a, \"to\": b}} or {{\"curve_min\", \"curve_max\"}}.");
        }

        public static AnimationCurve ToAnimationCurve(JToken token, string where)
        {
            if (token is JObject obj && obj["ease"] != null)
            {
                string ease = obj["ease"].Value<string>();
                if (!Easing.Exists(ease))
                    throw new RecipeException($"{where}: unknown ease '{ease}'. Valid: {string.Join(", ", Easing.Names)}.");
                return Easing.Curve(ease, obj["from"]?.Value<float>() ?? 0f, obj["to"]?.Value<float>() ?? 1f);
            }
            if (token is JObject withCurve && withCurve["curve"] != null)
                return ToAnimationCurve(withCurve["curve"], where);

            if (token is JArray keys && keys.Count > 0)
            {
                var frames = new List<Keyframe>();
                bool explicitTangents = false;
                foreach (var k in keys)
                {
                    if (!(k is JArray kv) || kv.Count < 2)
                        throw new RecipeException($"{where}: curve keys must be [time, value] or [time, value, inTangent, outTangent].");
                    var frame = new Keyframe(kv[0].Value<float>(), kv[1].Value<float>());
                    if (kv.Count >= 4)
                    {
                        frame.inTangent = kv[2].Value<float>();
                        frame.outTangent = kv[3].Value<float>();
                        explicitTangents = true;
                    }
                    frames.Add(frame);
                }
                var curve = new AnimationCurve(frames.OrderBy(f => f.time).ToArray());
                return explicitTangents ? curve : Easing.Smooth(curve);
            }
            throw new RecipeException($"{where}: expected curve keys [[t, v], ...] or {{\"ease\": name}}.");
        }

        static bool IsNumber(JToken t) => t.Type == JTokenType.Float || t.Type == JTokenType.Integer;

        // ---------------- gradients ----------------

        /// <summary>
        /// Color fields of particle modules. Shuriken stores particle colors as 8-bit Color32,
        /// so HDR values are clipped per channel (an HDR gold turns white). HDR colors are
        /// therefore normalized to full brightness with their hue kept, and a warning says to
        /// put the intensity in the material instead.
        /// </summary>
        public static ParticleSystem.MinMaxGradient ToMinMaxGradient(JToken token, RecipeContext ctx, string where)
        {
            if (token.Type == JTokenType.String)
                return new ParticleSystem.MinMaxGradient(Ldr(ToColor(token, ctx, where), ctx, where));

            // Two colors = random between them; each may be "#hex", "$name", {color, intensity} or [r, g, b, a].
            if (token is JArray arr && arr.Count == 2 && arr.All(t => t.Type == JTokenType.String || t.Type == JTokenType.Object || t.Type == JTokenType.Array))
                return new ParticleSystem.MinMaxGradient(Ldr(ToColor(arr[0], ctx, where + "[0]"), ctx, where + "[0]"), Ldr(ToColor(arr[1], ctx, where + "[1]"), ctx, where + "[1]"));

            if (token is JObject obj)
            {
                if (obj["gradient_min"] != null && obj["gradient_max"] != null)
                    return new ParticleSystem.MinMaxGradient(ToGradient(obj["gradient_min"], ctx, where + ".gradient_min", true), ToGradient(obj["gradient_max"], ctx, where + ".gradient_max", true));
                if (obj["gradient"] != null)
                    return new ParticleSystem.MinMaxGradient(ToGradient(obj["gradient"], ctx, where + ".gradient", true));
                if (obj["random_color"] != null)
                    return new ParticleSystem.MinMaxGradient(ToGradient(obj["random_color"], ctx, where + ".random_color", true)) { mode = ParticleSystemGradientMode.RandomColor };
                if (obj["colors"] != null)
                    return new ParticleSystem.MinMaxGradient(ToGradient(obj, ctx, where, true));
                if (obj["color"] != null || obj["hex"] != null)
                    return new ParticleSystem.MinMaxGradient(Ldr(ToColor(obj, ctx, where), ctx, where));
            }
            throw new RecipeException($"{where}: expected a color, [colorA, colorB], or {{\"gradient\": {{\"colors\": .., \"alphas\": ..}}}}.");
        }

        /// <summary>Scales an HDR color down to max channel 1 (keeping hue) and warns; LDR colors pass through.</summary>
        public static Color Ldr(Color c, RecipeContext ctx, string where)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (max <= 1f)
                return c;
            ctx.Warnings.Add($"{where}: HDR color ({Format(c.r)}, {Format(c.g)}, {Format(c.b)}) used on particles. Shuriken stores particle colors as 8-bit, " +
                             "so it would be clipped to white; the hue was kept at full brightness. Put the intensity in the material " +
                             "(HDR base/emission color) or a custom_data multiplier the shader reads.");
            return new Color(c.r / max, c.g / max, c.b / max, c.a);
        }

        public static Gradient ToGradient(JToken token, RecipeContext ctx, string where, bool particleColors = false)
        {
            if (!(token is JObject obj) || !(obj["colors"] is JArray colors) || colors.Count == 0)
                throw new RecipeException($"{where}: a gradient needs \"colors\": [[t, color], ...] or [color, ...].");

            var colorKeys = new List<GradientColorKey>();
            for (int i = 0; i < colors.Count; i++)
            {
                bool timed = colors[i] is JArray pair && pair.Count == 2 && IsNumber(pair[0]);
                var color = ToColor(timed ? colors[i][1] : colors[i], ctx, where);
                if (particleColors)
                    color = Ldr(color, ctx, $"{where}.colors[{i}]");
                float time = timed ? colors[i][0].Value<float>() : colors.Count == 1 ? 0f : i / (float)(colors.Count - 1);
                colorKeys.Add(new GradientColorKey(color, time));
            }

            var alphaKeys = new List<GradientAlphaKey>();
            if (obj["alphas"] is JArray alphas && alphas.Count > 0)
            {
                for (int i = 0; i < alphas.Count; i++)
                {
                    if (alphas[i] is JArray pair && pair.Count == 2)
                        alphaKeys.Add(new GradientAlphaKey(pair[1].Value<float>(), pair[0].Value<float>()));
                    else
                        alphaKeys.Add(new GradientAlphaKey(alphas[i].Value<float>(), alphas.Count == 1 ? 0f : i / (float)(alphas.Count - 1)));
                }
            }
            else
            {
                alphaKeys.Add(new GradientAlphaKey(1f, 0f));
                alphaKeys.Add(new GradientAlphaKey(1f, 1f));
            }

            if (colorKeys.Count > 8 || alphaKeys.Count > 8)
                throw new RecipeException($"{where}: Unity gradients support at most 8 color and 8 alpha keys.");

            var gradient = new Gradient();
            gradient.SetKeys(colorKeys.OrderBy(k => k.time).ToArray(), alphaKeys.OrderBy(k => k.time).ToArray());
            string mode = obj["mode"]?.Value<string>();
            if (!string.IsNullOrEmpty(mode))
                gradient.mode = (GradientMode)ToEnum(mode, typeof(GradientMode), where + ".mode");
            return gradient;
        }

        // ---------------- references ----------------

        static LayerMask ToLayerMask(JToken token, string where)
        {
            if (token.Type == JTokenType.Integer)
                return token.Value<int>();
            var names = token is JArray arr ? arr.Select(t => t.Value<string>()).ToArray() : new[] { token.Value<string>() };
            int mask = LayerMask.GetMask(names);
            if (mask == 0)
                throw new RecipeException($"{where}: none of the layers {string.Join(", ", names)} exist.");
            return mask;
        }

        static object ToObjectReference(JToken token, Type type, RecipeContext ctx, string where)
        {
            if (token.Type == JTokenType.Null)
                return null;

            string value = token.Value<string>();
            if (string.IsNullOrEmpty(value))
                return null;

            // References to systems in the same recipe resolve after creation.
            if (ctx.SystemNames.Contains(value) && (type == typeof(Transform) || type == typeof(GameObject) || typeof(Component).IsAssignableFrom(type)))
            {
                return new Deferred(() =>
                {
                    var go = ctx.System(value);
                    if (type == typeof(GameObject)) return go;
                    if (type == typeof(Transform)) return go.transform;
                    return go.GetComponent(type);
                });
            }

            if (typeof(Component).IsAssignableFrom(type))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(value);
                var component = prefab != null ? prefab.GetComponent(type) : null;
                if (component == null)
                    throw new RecipeException($"{where}: no {type.Name} on a prefab at '{value}'.");
                return component;
            }

            var asset = AssetDatabase.LoadAssetAtPath(value, type);
            if (asset == null)
            {
                // Sprites and sub-assets live inside other assets.
                asset = AssetDatabase.LoadAllAssetsAtPath(value).FirstOrDefault(a => type.IsInstanceOfType(a));
            }
            if (asset == null)
                throw new RecipeException($"{where}: no {type.Name} asset at '{value}'.");
            return asset;
        }

        public static string Format(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);
    }

    sealed class RecipeException : Exception
    {
        public RecipeException(string message) : base(message) { }
    }
}
