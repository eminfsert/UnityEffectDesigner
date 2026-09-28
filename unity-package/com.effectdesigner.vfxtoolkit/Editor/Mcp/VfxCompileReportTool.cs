#if VFXTOOLKIT_MCP
using EffectDesigner.VFXToolkit.Editor.Shaders;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace EffectDesigner.VFXToolkit.Editor.Mcp
{
    [McpForUnityTool(
        "vfx_compile_report",
        Description =
            "Re-import shaders and report compile errors and warnings with file and line, plus each shader's " +
            "properties (name, type, HDR). Pass .shader/.shadergraph paths, folders, or include files " +
            "(.hlsl/.cginc: every shader under Assets/ that includes it is checked). Optionally checks a property " +
            "contract, e.g. {\"_BaseMap\": \"Texture\", \"_TintColor\": \"HDRColor\", \"_Erosion\": \"Float\"}, and " +
            "lists missing properties and type mismatches. Call after every shader write; a shader with errors " +
            "or contract violations is not done.")]
    public static class VfxCompileReportTool
    {
        public class Parameters
        {
            [ToolParameter("Shader files (.shader/.shadergraph), include files (.hlsl/.cginc) or folders, as project paths.")]
            public string[] paths { get; set; }

            [ToolParameter("Property contract: {name: type} with types Color, HDRColor, Float, Range, Int, Vector, Texture; or an array of names.", Required = false)]
            public object expected_properties { get; set; }

            [ToolParameter("Force a synchronous re-import first so the report reflects the files on disk. Default true.", Required = false)]
            public bool? reimport { get; set; }

            [ToolParameter("Include compiler warnings. Default true.", Required = false)]
            public bool? include_warnings { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var result = ShaderReport.Run(@params, out string error);
            if (result == null)
                return new ErrorResponse(error, new { toolkitVersion = ToolkitInfo.Version });

            string message = result.shadersWithErrors == 0 && result.contractViolations == 0
                ? $"{result.checkedShaders} shader(s) compile cleanly" + (result.contractViolations == 0 ? " and match the property contract." : ".")
                : $"{result.shadersWithErrors} of {result.checkedShaders} shader(s) have errors; {result.contractViolations} property contract violation(s).";
            return new SuccessResponse(message, result);
        }
    }
}
#endif
