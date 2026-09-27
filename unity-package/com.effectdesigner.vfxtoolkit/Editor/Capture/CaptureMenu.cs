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
            string target = AssetDatabase.Contains(selected)
                ? AssetDatabase.GetAssetPath(selected)
                : selected.GetInstanceID().ToString();

            var request = CaptureRequest.FromJson(new Newtonsoft.Json.Linq.JObject
            {
                ["target"] = target,
                ["views"] = new Newtonsoft.Json.Linq.JArray("three_quarter", "side"),
                ["backgrounds"] = new Newtonsoft.Json.Linq.JArray("dark", "light"),
            }, out string error);

            var result = request != null ? TimelineCapture.Run(request, out error) : null;
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
