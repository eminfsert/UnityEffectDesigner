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
            Debug.Log($"[VFX Toolkit] Captured {result.frames.Count} frames. Contact sheet: {result.contactSheet}");
            EditorUtility.RevealInFinder(result.contactSheet);
        }

        [MenuItem(MenuPath, true)]
        static bool CanCapture() => Selection.activeGameObject != null;
    }
}
