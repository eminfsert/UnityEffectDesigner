using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EffectDesigner.VFXToolkit.Editor.Capture
{
    public sealed class CapturedFrame
    {
        public float time;
        public string view;
        public string background;
        public string path;
    }

    public sealed class ViewFramingInfo
    {
        public string view;
        public float[] lookAt;
        public float distance;
    }

    public sealed class TimelineCaptureResult
    {
        public string toolkitVersion = ToolkitInfo.Version;
        public string target;
        public string outputFolder;
        public string contactSheet;
        /// <summary>Row labels of the contact sheet, top to bottom ("view/background").</summary>
        public List<string> rows = new List<string>();
        public float[] times;
        public int frameSize;
        public List<CapturedFrame> frames = new List<CapturedFrame>();
        /// <summary>Alive particles (Shuriken + VFX Graph) at each sampled time.</summary>
        public List<int> particleCounts = new List<int>();
        /// <summary>Alive particles per system at each sampled time: shows when each layer (and each sub-emitter) is active.</summary>
        public Dictionary<string, List<int>> systemParticleCounts = new Dictionary<string, List<int>>();
        public float[] boundsCenter;
        public float framingRadius;
        /// <summary>Final camera placement per view (after auto framing). Pass it back as view_framing to keep the next capture's framing identical.</summary>
        public List<ViewFramingInfo> viewFraming = new List<ViewFramingInfo>();
        public int particleSystems;
        public int visualEffects;
        public int timeSampleables;
        /// <summary>Color measurements per time, for the first view on the first background (see colorStatsFor).</summary>
        public List<FrameColorStats> colorStats = new List<FrameColorStats>();
        public string colorStatsFor;
        /// <summary>Color measurements per time for the first view on every background, by background name: readability on light/ground colors.</summary>
        public Dictionary<string, List<FrameColorStats>> colorStatsByBackground = new Dictionary<string, List<FrameColorStats>>();
        /// <summary>
        /// Color measurements per time of each system rendered alone (first view, first background), by system label.
        /// Shows each layer's own hue and saturation over its life, without the other layers mixing in.
        /// </summary>
        public Dictionary<string, List<FrameColorStats>> systemColorStats = new Dictionary<string, List<FrameColorStats>>();
        /// <summary>Tonemapping and bloom in effect during the capture. Colors are only comparable to the game under the same settings.</summary>
        public PostProcessInfo postProcessing;
        /// <summary>Problems that likely need a fix.</summary>
        public List<string> warnings = new List<string>();
        /// <summary>Information about how the capture was made; nothing to fix.</summary>
        public List<string> notes = new List<string>();
    }

    /// <summary>
    /// Renders an effect at a list of times from a list of views and backgrounds, writing
    /// one PNG per frame plus a labelled contact sheet. This is the "eyes" of the
    /// Effect Designer agents: motion and timing can only be judged across time.
    /// </summary>
    public static class TimelineCapture
    {
        public static TimelineCaptureResult Run(CaptureRequest request, out string error)
        {
            var source = ResolveTarget(request.Target, out error);
            return source != null ? Run(request, source, out error) : null;
        }

        /// <summary>Captures <paramref name="source"/> directly; <see cref="CaptureRequest.Target"/> is only used as a label.</summary>
        public static TimelineCaptureResult Run(CaptureRequest request, GameObject source, out string error)
        {
            error = null;
            var result = new TimelineCaptureResult
            {
                target = request.Target,
                times = request.Times,
                frameSize = request.FrameSize,
            };

            string profileSource = ResolveVolumeProfile(request, out error);
            if (error != null)
                return null;

            string folder = PrepareOutputFolder(request, source.name);
            result.outputFolder = folder;

            try
            {
                using (var rig = new CaptureRig(source, request.FrameSize, request.FieldOfView, request.AddLight, request.PostProcessing, request.VolumeProfile))
                {
                    var sampler = new EffectSampler(rig.Effect, request.Seed);
                    result.particleSystems = sampler.ParticleSystemCount;
                    result.visualEffects = sampler.VisualEffectCount;
                    result.timeSampleables = sampler.SampleableCount;

                    if (sampler.ParticleSystemCount == 0 && sampler.VisualEffectCount == 0 && sampler.SampleableCount == 0)
                        result.warnings.Add("No ParticleSystem, VisualEffect or IVfxTimeSampleable found; all frames will look the same.");
                    if (sampler.VisualEffectCount > 0)
                        result.warnings.Add("VisualEffect (VFX Graph) edit-mode stepping is experimental; if frames look empty or identical, verify in Play Mode.");

                    var labels = sampler.SystemLabels();
                    foreach (var label in labels)
                        result.systemParticleCounts[label] = new List<int>();

                    // Pass 1: sample every time to measure bounds, so framing is identical across frames.
                    Bounds total = default;
                    bool hasBounds = false;
                    foreach (float t in request.Times)
                    {
                        sampler.SampleAt(t);
                        result.particleCounts.Add(sampler.CurrentParticleCount());
                        var perSystem = sampler.CurrentCountsPerSystem();
                        for (int i = 0; i < labels.Count; i++)
                            result.systemParticleCounts[labels[i]].Add(perSystem[i]);
                        if (sampler.TryGetVisibleBounds(out var b))
                        {
                            if (!hasBounds) { total = b; hasBounds = true; }
                            else total.Encapsulate(b);
                        }
                    }

                    // A sub-emitter that never has particles while its parent does usually means the
                    // trigger never fired (e.g. its parent's particles outlive the sampled times).
                    foreach (var (parent, child) in sampler.SubEmitterLinks())
                    {
                        var parentCounts = result.systemParticleCounts[labels[parent]];
                        var childCounts = result.systemParticleCounts[labels[child]];
                        if (parentCounts.Exists(c => c > 0) && childCounts.TrueForAll(c => c == 0))
                            result.warnings.Add($"Sub-emitter '{labels[child]}' of '{labels[parent]}' had no particles at any sampled time. " +
                                                "Check its trigger/probability and that the sampled times cover when it fires; if it looks right, verify in Play Mode.");
                    }

                    Vector3 center = hasBounds ? total.center : rig.Effect.transform.position;
                    float radius = request.FramingRadius > 0f
                        ? request.FramingRadius
                        : (hasBounds ? Mathf.Max(0.1f, total.extents.magnitude) : 1f);
                    if (!hasBounds && request.FramingRadius <= 0f)
                        result.warnings.Add("Nothing visible at any sampled time; used a 1 m framing radius.");
                    result.boundsCenter = new[] { center.x, center.y, center.z };
                    result.framingRadius = radius;

                    var framings = new ViewFraming[request.Views.Count];
                    for (int v = 0; v < framings.Length; v++)
                        framings[v] = new ViewFraming { Center = center, Distance = rig.DistanceToFit(radius), DepthRadius = radius };

                    // Reused placements (view_framing) keep iterations comparable, so those views are not re-framed.
                    var fixedViews = new bool[framings.Length];
                    for (int v = 0; v < framings.Length; v++)
                    {
                        if (request.ViewFraming.TryGetValue(request.Views[v].Name, out var reuse))
                        {
                            framings[v].Center = reuse.lookAt;
                            framings[v].Distance = reuse.distance;
                            fixedViews[v] = true;
                        }
                    }
                    foreach (var name in request.ViewFraming.Keys)
                        if (!request.Views.Exists(view => string.Equals(view.Name, name, StringComparison.OrdinalIgnoreCase)))
                            result.warnings.Add($"view_framing names view '{name}', which this capture does not render; it was ignored.");
                    int reused = fixedViews.Count(f => f);
                    if (reused > 0)
                        result.notes.Add($"Reused the given camera placement for {reused} of {framings.Length} view(s); they were not auto-framed.");

                    // A fixed framing radius means "keep scale comparable between captures", so skip auto framing then.
                    if (request.AutoFrame && request.FramingRadius <= 0f && hasBounds && reused < framings.Length)
                        AutoFraming.Refine(rig, sampler, request.Views, request.Times, request.Backgrounds[0].Color, framings, result.warnings, fixedViews);

                    for (int v = 0; v < framings.Length; v++)
                    {
                        var c = framings[v].Center;
                        result.viewFraming.Add(new ViewFramingInfo { view = request.Views[v].Name, lookAt = new[] { c.x, c.y, c.z }, distance = framings[v].Distance });
                    }

                    foreach (var view in request.Views)
                        foreach (var bg in request.Backgrounds)
                            result.rows.Add($"{view.Name}/{bg.Name}");

                    // Background-only references for color stats: first view, every background.
                    rig.Aim(framings[0].Center, framings[0].Distance, framings[0].DepthRadius, request.Views[0]);
                    var statsReferences = new Color32[request.Backgrounds.Count][];
                    for (int b = 0; b < statsReferences.Length; b++)
                    {
                        statsReferences[b] = rig.RenderBackgroundOnly(request.Backgrounds[b].Color).GetPixels32();
                        result.colorStatsByBackground[request.Backgrounds[b].Name] = b == 0 ? result.colorStats : new List<FrameColorStats>();
                    }
                    result.colorStatsFor = result.rows[0];

                    var systemRenderers = request.SystemColorStats ? sampler.SystemRenderers() : new List<Renderer>();
                    int drawnSystems = systemRenderers.Count(r => r != null);
                    // One system alone is the whole effect; measuring it again would say nothing new.
                    if (drawnSystems < 2)
                        systemRenderers.Clear();
                    for (int i = 0; i < systemRenderers.Count; i++)
                        if (systemRenderers[i] != null)
                            result.systemColorStats[labels[i]] = new List<FrameColorStats>();
                    result.postProcessing = rig.DescribePostProcessing();
                    result.postProcessing.volumeProfileSource = profileSource;
                    if (profileSource == null)
                        result.warnings.Add($"Rendered with the pipeline's default volume profiles only (global + quality level; tonemapping: {result.postProcessing.tonemapping}). " +
                                            "Scene volumes never reach captures, so colors may not match the game: pass volume_profile, or set it once " +
                                            "for the project (select the game's VolumeProfile > Assets > Effect Designer > Use As Capture Volume Profile).");

                    // Pass 2: render. Sampling once per time and rendering all views keeps it cheap.
                    var sheet = new ContactSheet(request.Times, result.rows.Count, request.FrameSize);
                    for (int column = 0; column < request.Times.Length; column++)
                    {
                        float t = request.Times[column];
                        sampler.SampleAt(t);

                        int row = 0;
                        for (int v = 0; v < request.Views.Count; v++)
                        {
                            var view = request.Views[v];
                            rig.Aim(framings[v].Center, framings[v].Distance, framings[v].DepthRadius, view);
                            foreach (var bg in request.Backgrounds)
                            {
                                var frame = rig.Render(bg.Color);
                                string file = Path.Combine(folder, $"{Sanitize(view.Name)}_{Sanitize(bg.Name)}_t{t.ToString("0.000", CultureInfo.InvariantCulture)}.png");
                                File.WriteAllBytes(file, frame.EncodeToPNG());
                                sheet.Place(frame, row, column);
                                if (v == 0)
                                {
                                    int b = row;
                                    result.colorStatsByBackground[bg.Name].Add(ColorStats.Measure(frame.GetPixels32(), statsReferences[b], t));
                                }
                                result.frames.Add(new CapturedFrame { time = t, view = view.Name, background = bg.Name, path = file });
                                row++;
                            }
                        }

                        // Each system alone, first view on the first background. Systems with no particles
                        // right now are measured as empty without rendering.
                        if (systemRenderers.Count > 0)
                        {
                            rig.Aim(framings[0].Center, framings[0].Distance, framings[0].DepthRadius, request.Views[0]);
                            for (int i = 0; i < systemRenderers.Count; i++)
                            {
                                if (systemRenderers[i] == null)
                                    continue;
                                FrameColorStats stats;
                                if (result.systemParticleCounts[labels[i]][column] == 0 && systemRenderers[i] is ParticleSystemRenderer)
                                {
                                    stats = new FrameColorStats { time = t, hue = -1f };
                                }
                                else
                                {
                                    using (sampler.Isolate(systemRenderers[i]))
                                        stats = ColorStats.Measure(rig.Render(request.Backgrounds[0].Color).GetPixels32(), statsReferences[0], t);
                                }
                                result.systemColorStats[labels[i]].Add(stats);
                            }
                        }
                    }

                    WarnAboutColor(result);
                    WarnAboutReadability(result);

                    string sheetPath = Path.Combine(folder, "contact_sheet.png");
                    File.WriteAllBytes(sheetPath, sheet.EncodePng());
                    result.contactSheet = sheetPath;
                    if (sheet.CellSize < request.FrameSize)
                        result.notes.Add($"Contact sheet cells were downscaled to {sheet.CellSize}px to stay within {ContactSheet.MaxSheetWidth}px; full-size frames are in the output folder.");
                }
            }
            catch (Exception ex)
            {
                error = $"Capture failed: {ex.GetType().Name}: {ex.Message}";
                return null;
            }
            finally
            {
                EffectSampler.ClearCaptureGlobals();
            }

            return result;
        }

        const string ProjectSettingsSource = "project settings (ProjectSettings/EffectDesigner.json)";

        /// <summary>
        /// Picks the volume profile: the parameter, "none" to opt out (pipeline defaults only, e.g. for
        /// before/after comparisons), or the project default. Returns where it came from (null = no
        /// profile, not requested either) and fails early, naming the source, if the asset is missing.
        /// </summary>
        static string ResolveVolumeProfile(CaptureRequest request, out string error)
        {
            error = null;
            string source;
            if (string.Equals(request.VolumeProfile?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
            {
                request.VolumeProfile = null;
                return "none (explicit)";
            }
            if (!string.IsNullOrWhiteSpace(request.VolumeProfile))
            {
                source = "volume_profile parameter";
            }
            else
            {
                request.VolumeProfile = EffectDesignerSettings.Load().VolumeProfile;
                if (string.IsNullOrWhiteSpace(request.VolumeProfile))
                {
                    request.VolumeProfile = null;
                    return null;
                }
                source = ProjectSettingsSource;
            }

            if (AssetDatabase.LoadMainAssetAtPath(request.VolumeProfile) == null)
            {
                error = $"No VolumeProfile at '{request.VolumeProfile}' (from the {source}). " +
                        (source == ProjectSettingsSource
                            ? "The profile was moved or deleted: select the game's VolumeProfile and use Assets > Effect Designer > Use As Capture Volume Profile, " +
                              "edit ProjectSettings/EffectDesigner.json, or pass volume_profile \"none\" for this capture."
                            : "Pass the asset path of an existing VolumeProfile, or \"none\".");
                return null;
            }
            return source;
        }

        /// <summary>
        /// Flags washed-out effects, the most common stylized-VFX failure: most measured frames
        /// having mostly colorless bright pixels. A deliberately white core flash in one or two
        /// frames does not trigger it.
        /// </summary>
        static void WarnAboutColor(TimelineCaptureResult result)
        {
            int measured = 0, washed = 0;
            float worst = 0f;
            foreach (var s in result.colorStats)
            {
                if (s.washedOut < 0f) continue;
                measured++;
                if (s.washedOut >= 0.5f) washed++;
                worst = Mathf.Max(worst, s.washedOut);
            }
            if (measured > 0 && washed * 2 > measured)
                result.warnings.Add($"Washed out: in {washed} of {measured} frames most bright pixels have lost their color (up to {Pct(worst)}). " +
                                    "HDR intensity is too high or the tint is white. Use saturated tints with lower intensity, and keep " +
                                    "white for a small core layer.");
        }

        /// <summary>
        /// Readability across backgrounds: an effect that stands out on dark but barely changes the
        /// pixels of a light or ground-colored background (typical for additive blending) is measured
        /// here as much lower coverage on that background than on the best one.
        /// </summary>
        internal static void WarnAboutReadability(TimelineCaptureResult result)
        {
            if (result.colorStatsByBackground.Count < 2)
                return;
            var means = result.colorStatsByBackground.ToDictionary(e => e.Key, e => e.Value.Count > 0 ? e.Value.Average(s => s.coverage) : 0f);
            var best = means.OrderByDescending(e => e.Value).First();
            if (best.Value <= 0f)
                return;
            foreach (var entry in means)
            {
                float ratio = entry.Value / best.Value;
                if (ratio < ReadableCoverageRatio)
                    result.warnings.Add($"Low contrast on '{entry.Key}': the effect changes {Pct(ratio)} as many pixels as on '{best.Key}' " +
                                        $"(mean coverage {Pct(entry.Value, 1)} vs {Pct(best.Value, 1)}). Additive layers vanish on light backgrounds: " +
                                        "add an alpha-blended or darker outline/shadow layer, or raise saturation.");
            }
        }

        /// <summary>"45%" in every culture (a Turkish editor would print "%45", German "45 %").</summary>
        static string Pct(float share, int decimals = 0) =>
            (share * 100f).ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), CultureInfo.InvariantCulture) + "%";

        /// <summary>Coverage on a background, relative to the best background, below which the effect counts as hard to read.</summary>
        public const float ReadableCoverageRatio = 0.5f;

        /// <summary>
        /// Accepts a prefab/asset path ("Assets/...prefab"), a scene hierarchy path
        /// ("Root/Child"), a GameObject name, or an instance id.
        /// </summary>
        public static GameObject ResolveTarget(string target, out string error)
        {
            error = null;
            if (target.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || target.StartsWith("Assets/", StringComparison.Ordinal) || target.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(target);
                if (asset == null)
                    error = $"No GameObject/prefab asset at '{target}'.";
                return asset;
            }

            if (int.TryParse(target, out int instanceId))
            {
                if (ObjectIdCompat.FromInstanceId(instanceId) is GameObject byId)
                    return byId;
            }

            var candidates = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == target || HierarchyPath(t) == target)
                            candidates.Add(t.gameObject);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                error = $"No scene GameObject named or at path '{target}', and it is not a prefab path.";
                return null;
            }
            // Prefer exact path matches, then objects that actually contain effects.
            var exact = candidates.FirstOrDefault(c => HierarchyPath(c.transform) == target);
            if (exact != null)
                return exact;
            return candidates.FirstOrDefault(c => c.GetComponentInChildren<ParticleSystem>(true) != null) ?? candidates[0];
        }

        static string HierarchyPath(Transform t)
        {
            string path = t.name;
            for (var p = t.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }

        static string PrepareOutputFolder(CaptureRequest request, string effectName)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string root = Path.IsPathRooted(request.OutputFolder)
                ? request.OutputFolder
                : Path.Combine(projectRoot, request.OutputFolder);
            string label = string.IsNullOrWhiteSpace(request.Label) ? effectName : request.Label;
            string folder = Path.Combine(root, $"{Sanitize(label)}_{DateTime.Now:yyyyMMdd_HHmmss_fff}");
            Directory.CreateDirectory(folder);
            return Path.GetFullPath(folder);
        }

        static string Sanitize(string value)
        {
            var chars = value.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray();
            return new string(chars);
        }
    }
}
