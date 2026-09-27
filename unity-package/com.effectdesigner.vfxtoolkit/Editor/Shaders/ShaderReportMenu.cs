using System.Linq;
using System.Text;
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

            var log = new StringBuilder($"[VFX Toolkit] {result.checkedShaders} shader(s), {result.shadersWithErrors} with errors.");
            foreach (var shader in result.shaders)
            {
                log.Append($"\n\n{shader.name} ({shader.path}): {shader.errors.Count} error(s), {shader.warnings.Count} warning(s)");
                foreach (var e in shader.errors)
                    log.Append($"\n  ERROR {e.file}:{e.line} {e.message}");
                foreach (var w in shader.warnings)
                    log.Append($"\n  warning {w.file}:{w.line} {w.message}");
                log.Append("\n  properties: ").Append(string.Join(", ", shader.properties.Select(p => $"{p.name} ({p.type}{(p.hdr ? ", HDR" : "")})")));
            }

            if (result.shadersWithErrors > 0) Debug.LogError(log.ToString());
            else Debug.Log(log.ToString());
        }

        [MenuItem(MenuPath, true)]
        static bool CanReport() => Selection.objects.Length > 0;
    }
}
