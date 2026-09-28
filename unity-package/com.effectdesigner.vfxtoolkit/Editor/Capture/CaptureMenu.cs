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
            var table = new System.Text.StringBuilder("time        " + string.Join(" ", System.Array.ConvertAll(result.times, t => ContactSheet.FormatTime(t).PadLeft(6))));
            foreach (var entry in result.systemParticleCounts)
                table.Append("\n").Append(entry.Key.PadRight(12).Substring(0, 12)).Append(string.Join(" ", entry.Value.ConvertAll(c => c.ToString().PadLeft(6))));
            if (result.postProcessing != null)
                table.Append($"\n\nPost-processing: {result.postProcessing.volumeProfile}, tonemapping {result.postProcessing.tonemapping}, bloom {(result.postProcessing.bloom ? $"threshold {result.postProcessing.bloomThreshold:0.##} intensity {result.postProcessing.bloomIntensity:0.##}" : "off")}");
            table.Append($"\n\nColor ({result.colorStatsFor}): washed-out / saturation / hue");
            foreach (var c in result.colorStats)
                table.Append($"\n{ContactSheet.FormatTime(c.time),6}  {(c.washedOut < 0 ? "-" : c.washedOut.ToString("P0")),5}  {c.saturation,4:0.00}  {(c.hue < 0 ? "-" : c.hue.ToString("0") + "°")}");
            Debug.Log($"[VFX Toolkit] Captured {result.frames.Count} frames. Contact sheet: {result.contactSheet}\nAlive particles per system:\n{table}");
            EditorUtility.RevealInFinder(result.contactSheet);
        }

        [MenuItem(MenuPath, true)]
        static bool CanCapture() => Selection.activeGameObject != null;
    }
}
