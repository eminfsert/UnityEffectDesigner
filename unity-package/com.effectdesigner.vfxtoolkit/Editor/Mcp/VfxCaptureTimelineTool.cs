#if VFXTOOLKIT_MCP
using EffectDesigner.VFXToolkit.Editor.Capture;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace EffectDesigner.VFXToolkit.Editor.Mcp
{
    [McpForUnityTool(
        "vfx_capture_timeline",
        Description =
            "Render a VFX (Shuriken and/or VFX Graph) at several points in time, in edit mode and " +
            "deterministically (fixed seeds, played frame by frame at 60 fps from t=0, sub-emitters included), inside an isolated preview scene. " +
            "Writes one PNG per time x view x background and a contact sheet (columns = times, labelled; " +
            "rows = view/background). Returns absolute file paths: open the contact sheet image to judge " +
            "timing, shape, readability and color. Also returns alive particle counts per system and time, " +
            "color measurements (coverage, washedOut, saturation, hue) per time for every background and for each system rendered alone, " +
            "the camera placement per view (pass it back as view_framing to compare iterations at identical framing), warnings (likely problems) " +
            "and notes (information). View presets: front, back, side, top, three_quarter, low; or " +
            "{name, azimuth, elevation} in degrees (azimuth 0 = camera on -Z). Backgrounds: dark, mid, light " +
            "or hex colors.")]
    public static class VfxCaptureTimelineTool
    {
        public class Parameters
        {
            [ToolParameter("Effect to capture: prefab path (Assets/...prefab), scene hierarchy path, GameObject name, or instance id.")]
            public string target { get; set; }

            [ToolParameter("Times in seconds since the effect started; played at 60 fps, 0 is the first frame. Default [0,0.05,0.1,0.2,0.35,0.5,0.75,1,1.5]. Max 24.", Required = false)]
            public float[] times { get; set; }

            [ToolParameter("Camera views: preset names or {name, azimuth, elevation} objects. Default [\"three_quarter\"].", Required = false)]
            public object[] views { get; set; }

            [ToolParameter("Backgrounds: dark, mid, light or hex colors. Default [\"dark\"]. Use [\"dark\",\"light\"] to check readability.", Required = false)]
            public string[] backgrounds { get; set; }

            [ToolParameter("Frame size in pixels (square), 64-1024. Default 320.", Required = false)]
            public int? frame_size { get; set; }

            [ToolParameter("Random seed applied to every particle system / visual effect. Default 1234.", Required = false)]
            public int? seed { get; set; }

            [ToolParameter("Fixed framing radius in meters; disables auto framing so scale stays comparable between iterations. Default: auto.", Required = false)]
            public float? framing_radius { get; set; }

            [ToolParameter("Re-frame each view on the pixels the effect actually covers across all times (centred, ~80% of the frame). Default true.", Required = false)]
            public bool? auto_frame { get; set; }

            [ToolParameter("Camera placements to reuse, e.g. a previous capture's viewFraming: [{view, lookAt: [x,y,z], distance}]. Listed views skip auto framing, so before/after captures line up.", Required = false)]
            public object[] view_framing { get; set; }

            [ToolParameter("Also render each system alone (first view, first background) and return its colors in systemColorStats. Default true; costs one extra render per system per time.", Required = false)]
            public bool? system_color_stats { get; set; }

            [ToolParameter("With system_color_stats: also save each system-alone render as a PNG (systemFrames), to compare a layer alone with the composite frame. Default false.", Required = false)]
            public bool? system_frames { get; set; }

            [ToolParameter("Vertical field of view in degrees. Default 35.", Required = false)]
            public float? fov { get; set; }

            [ToolParameter("Add a neutral key light for lit materials. Default true.", Required = false)]
            public bool? add_light { get; set; }

            [ToolParameter("Render URP post-processing (bloom, tonemapping) from the project's global volumes. Default true.", Required = false)]
            public bool? post_processing { get; set; }

            [ToolParameter("VolumeProfile asset (Assets/...asset) of the game scene the effect plays in, applied as a top-priority global volume so post-processing matches the game. Defaults to volume_profile in ProjectSettings/EffectDesigner.json; \"none\" renders with the pipeline's default profiles only (for before/after comparisons). Scene volumes never reach captures.", Required = false)]
            public string volume_profile { get; set; }

            [ToolParameter("Output folder (project-relative or absolute). Default Library/VFXToolkit/Captures.", Required = false)]
            public string output_folder { get; set; }

            [ToolParameter("Name for this capture's subfolder, e.g. 'arcane_nova_iter2'. Default: effect name.", Required = false)]
            public string label { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var request = CaptureRequest.FromJson(@params, out string error);
            if (request == null)
                return new ErrorResponse(error, new { toolkitVersion = ToolkitInfo.Version });

            var result = TimelineCapture.Run(request, out error);
            if (result == null)
                return new ErrorResponse(error, new { toolkitVersion = ToolkitInfo.Version });

            return new SuccessResponse(
                $"Captured {result.frames.Count} frames of '{result.target}'. Contact sheet: {result.contactSheet}",
                result);
        }
    }
}
#endif
