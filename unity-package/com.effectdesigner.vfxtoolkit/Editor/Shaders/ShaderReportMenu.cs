using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor.Shaders
{
    /// <summary>Manual entry point so the compile report can be tried without an MCP client.</summary>
    static class ShaderReportMenu
    {
        const string MenuPath = "Tools/Effect Designer/Shader Compile Report For Selection";

        [MenuItem(MenuPath)]
        static void ReportSelection()
        {
            var paths = Selection.objects.Select(AssetDatabase.GetAssetPath).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
            var result = ShaderReport.Run(new JObject { ["paths"] = new JArray(paths) }, out string error);
            if (result == null)
            {
                Debug.LogError($"[VFX Toolkit] {error}");
                return;
            }

            foreach (var warning in result.warnings)
                Debug.LogWarning($"[VFX Toolkit] {warning}");

            // One entry per line: MCP read_console only returns the first lines of an entry.
            Debug.Log($"[VFX Toolkit] {result.checkedShaders} shader(s), {result.shadersWithErrors} with errors.");
            foreach (var shader in result.shaders)
            {
                Debug.Log($"[VFX Toolkit] {shader.name} ({shader.path}): {shader.errors.Count} error(s), {shader.warnings.Count} warning(s)");
                foreach (var e in shader.errors)
                    Debug.LogError($"[VFX Toolkit] {shader.name} ERROR {e.file}:{e.line} {e.message}");
                foreach (var w in shader.warnings)
                    Debug.LogWarning($"[VFX Toolkit] {shader.name} warning {w.file}:{w.line} {w.message}");
                Debug.Log($"[VFX Toolkit] {shader.name} properties: " +
                          string.Join(", ", shader.properties.Select(p => $"{p.name} ({p.type}{(p.hdr ? ", HDR" : "")})")));
            }
        }

        [MenuItem(MenuPath, true)]
        static bool CanReport() => Selection.objects.Length > 0;
    }
}
