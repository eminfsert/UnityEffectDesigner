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

    public sealed class TimelineCaptureResult
    {
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
        public float[] boundsCenter;
        public float framingRadius;
        public int particleSystems;
        public int visualEffects;
        public int timeSampleables;
        public List<string> warnings = new List<string>();
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
            error = null;
            var source = ResolveTarget(request.Target, out error);
            if (source == null)
                return null;

            var result = new TimelineCaptureResult
            {
                target = request.Target,
                times = request.Times,
                frameSize = request.FrameSize,
            };

            string folder = PrepareOutputFolder(request, source.name);
            result.outputFolder = folder;

            try
            {
                using (var rig = new CaptureRig(source, request.FrameSize, request.FieldOfView, request.AddLight, request.PostProcessing))
                {
                    var sampler = new EffectSampler(rig.Effect, request.Seed);
                    result.particleSystems = sampler.ParticleSystemCount;
                    result.visualEffects = sampler.VisualEffectCount;
                    result.timeSampleables = sampler.SampleableCount;

                    if (sampler.ParticleSystemCount == 0 && sampler.VisualEffectCount == 0 && sampler.SampleableCount == 0)
                        result.warnings.Add("No ParticleSystem, VisualEffect or IVfxTimeSampleable found; all frames will look the same.");
                    if (sampler.VisualEffectCount > 0)
                        result.warnings.Add("VisualEffect (VFX Graph) edit-mode stepping is experimental; if frames look empty or identical, verify in Play Mode.");

                    // Pass 1: sample every time to measure bounds, so framing is identical across frames.
                    Bounds total = default;
                    bool hasBounds = false;
                    foreach (float t in request.Times)
                    {
                        sampler.SampleAt(t);
                        result.particleCounts.Add(sampler.CurrentParticleCount());
                        if (sampler.TryGetVisibleBounds(out var b))
                        {
                            if (!hasBounds) { total = b; hasBounds = true; }
                            else total.Encapsulate(b);
                        }
                    }

                    Vector3 center = hasBounds ? total.center : rig.Effect.transform.position;
                    float radius = request.FramingRadius > 0f
                        ? request.FramingRadius
                        : (hasBounds ? Mathf.Max(0.1f, total.extents.magnitude) : 1f);
                    if (!hasBounds && request.FramingRadius <= 0f)
                        result.warnings.Add("Nothing visible at any sampled time; used a 1 m framing radius.");
                    result.boundsCenter = new[] { center.x, center.y, center.z };
                    result.framingRadius = radius;

                    foreach (var view in request.Views)
                        foreach (var bg in request.Backgrounds)
                            result.rows.Add($"{view.Name}/{bg.Name}");

                    // Pass 2: render. Sampling once per time and rendering all views keeps it cheap.
                    var sheet = new ContactSheet(request.Times, result.rows.Count, request.FrameSize);
                    for (int column = 0; column < request.Times.Length; column++)
                    {
                        float t = request.Times[column];
                        sampler.SampleAt(t);

                        int row = 0;
                        foreach (var view in request.Views)
                        {
                            rig.Aim(center, radius, view);
                            foreach (var bg in request.Backgrounds)
                            {
                                var frame = rig.Render(bg.Color);
                                string file = Path.Combine(folder, $"{Sanitize(view.Name)}_{Sanitize(bg.Name)}_t{t.ToString("0.000", CultureInfo.InvariantCulture)}.png");
                                File.WriteAllBytes(file, frame.EncodeToPNG());
                                sheet.Place(frame, row, column);
                                result.frames.Add(new CapturedFrame { time = t, view = view.Name, background = bg.Name, path = file });
                                row++;
                            }
                        }
                    }

                    string sheetPath = Path.Combine(folder, "contact_sheet.png");
                    File.WriteAllBytes(sheetPath, sheet.EncodePng());
                    result.contactSheet = sheetPath;
                    if (sheet.CellSize < request.FrameSize)
                        result.warnings.Add($"Contact sheet cells were downscaled to {sheet.CellSize}px to stay within {ContactSheet.MaxSheetWidth}px; full-size frames are in the output folder.");
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
                if (EditorUtility.InstanceIDToObject(instanceId) is GameObject byId)
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
