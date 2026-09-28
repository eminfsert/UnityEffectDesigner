using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor.Capture
{
    /// <summary>Manual entry point so the capture can be tried without an MCP client.</summary>
    static class CaptureMenu
    {
        const string MenuPath = "Tools/Effect Designer/Capture Timeline Of Selection";

        [MenuItem(MenuPath)]
        static void CaptureSelection()
        {
            var selected = Selection.activeGameObject;
            // The object is passed directly; the target string only names the capture.
            string target = AssetDatabase.Contains(selected)
                ? AssetDatabase.GetAssetPath(selected)
                : selected.name;

            var request = CaptureRequest.FromJson(new JObject
            {
                ["target"] = target,
                ["views"] = new JArray("three_quarter", "side"),
                ["backgrounds"] = new JArray("dark", "light"),
            }, out string error);

            var result = request != null ? TimelineCapture.Run(request, selected, out error) : null;
            if (result == null)
            {
                Debug.LogError($"[VFX Toolkit] {error}");
                return;
            }

            foreach (var warning in result.warnings)
                Debug.LogWarning($"[VFX Toolkit] {warning}");
            foreach (var note in result.notes)
                Debug.Log($"[VFX Toolkit] Note: {note}");
            foreach (var line in SummaryLines(result))
                Debug.Log("[VFX Toolkit] " + line);
            EditorUtility.RevealInFinder(result.contactSheet);
        }

        /// <summary>
        /// One log entry per line, culture-invariant: tools like MCP read_console return only the
        /// first lines of a multi-line entry (the rest lands in the stack trace), and a Turkish or
        /// German editor would otherwise print "0,95" and "%0".
        /// </summary>
        internal static IEnumerable<string> SummaryLines(TimelineCaptureResult result)
        {
            // Every interpolation with a number goes through Inv: a nested $"..." is formatted with the
            // current culture before an outer FormattableString.Invariant ever sees it.
            yield return $"Captured {result.frames.Count} frames (toolkit {result.toolkitVersion}). Contact sheet: {result.contactSheet}";
            yield return "Times: " + string.Join(" ", result.times.Select(ContactSheet.FormatTime));
            foreach (var entry in result.systemParticleCounts)
                yield return $"Particles {entry.Key}: {string.Join(" ", entry.Value)}";
            if (result.postProcessing != null)
            {
                var p = result.postProcessing;
                string bloom = p.bloom ? Inv($"threshold {p.bloomThreshold:0.##} intensity {p.bloomIntensity:0.##}") : "off";
                yield return $"Post-processing: {p.volumeProfile} (source: {p.volumeProfileSource ?? "none set"}), tonemapping {p.tonemapping}, bloom {bloom}";
            }
            var byBackground = result.colorStatsByBackground.Count > 0
                ? result.colorStatsByBackground.Select(e => (label: $"{(result.colorStatsFor ?? "").Split('/')[0]}/{e.Key}", stats: e.Value))
                : new[] { (label: result.colorStatsFor, stats: result.colorStats) };
            foreach (var (label, stats) in byBackground)
                foreach (var c in stats)
                    yield return ColorLine(label, c);
            foreach (var entry in result.systemColorStats)
                foreach (var c in entry.Value)
                    yield return ColorLine(entry.Key + " alone", c);
        }

        static string ColorLine(string label, FrameColorStats c)
        {
            string washed = c.washedOut < 0 ? "-" : Inv($"{c.washedOut * 100:0}%");
            string hue = c.hue < 0 ? "-" : Inv($"{c.hue:0}deg");
            return $"Color {label} {ContactSheet.FormatTime(c.time)}: washedOut {washed}, " +
                   Inv($"saturation {c.saturation:0.00}, ") + $"hue {hue}, " + Inv($"coverage {c.coverage * 100:0.00}%");
        }

        static string Inv(FormattableString s) => FormattableString.Invariant(s);

        [MenuItem(MenuPath, true)]
        static bool CanCapture() => Selection.activeGameObject != null;
    }
}
