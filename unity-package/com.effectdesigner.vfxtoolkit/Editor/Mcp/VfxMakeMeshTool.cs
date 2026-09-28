#if VFXTOOLKIT_MCP
using EffectDesigner.VFXToolkit.Editor.Meshes;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace EffectDesigner.VFXToolkit.Editor.Mcp
{
    [McpForUnityTool(
        "vfx_make_mesh",
        Description =
            "Create or rebuild a procedural VFX mesh asset (.asset) for mesh particles and shells: " +
            "dome (sphere cap sitting on y=0, hemisphere by default: explosion domes, shields), sphere, " +
            "ring (flat annulus on the ground: shockwaves, ground cracks), cylinder (open tube or cone/funnel: " +
            "pillars, tornados, shockwave walls), arc (tapered band along a circular arc: slashes, crescents, ink swipes). " +
            "UVs: u runs around/along the shape (0..1), v across it (0..1), so shaders can erode, scroll and color by them. " +
            "Rebuilding an existing path keeps its GUID and references. Returns vertex/triangle counts, bounds and the UV layout.")]
    public static class VfxMakeMeshTool
    {
        public class Parameters
        {
            [ToolParameter("Shape: dome, sphere, ring, cylinder or arc.")]
            public string shape { get; set; }

            [ToolParameter("Asset path under Assets/ ending in .asset, e.g. Assets/VFX/Boom/Meshes/SM_Boom_Dome.asset.")]
            public string path { get; set; }

            [ToolParameter("dome/sphere/cylinder/arc radius in meters (arc: to the band's middle). Default 0.5 (1 m across), so particle start_size scales it.", Required = false)]
            public float? radius { get; set; }

            [ToolParameter("dome: cap angle from the top in degrees, 90 = hemisphere (default), 180 = full sphere.", Required = false)]
            public float? angle { get; set; }

            [ToolParameter("ring: inner and outer radius. Defaults 0.35 and 0.5.", Required = false)]
            public float? inner_radius { get; set; }

            [ToolParameter("ring: outer radius. Default 0.5.", Required = false)]
            public float? outer_radius { get; set; }

            [ToolParameter("cylinder: height (default 1) and top_radius (default = radius; smaller for a cone, larger for a funnel).", Required = false)]
            public float? height { get; set; }

            [ToolParameter("cylinder: top radius. Default = radius.", Required = false)]
            public float? top_radius { get; set; }

            [ToolParameter("arc: arc length in degrees (default 150), width (default 0.15) and taper 0..1 (1 = pointed tips, default).", Required = false)]
            public float? arc { get; set; }

            [ToolParameter("arc: band width. Default 0.15.", Required = false)]
            public float? width { get; set; }

            [ToolParameter("arc: 0 = constant width, 1 = pointed tips (default).", Required = false)]
            public float? taper { get; set; }

            [ToolParameter("Subdivisions around/along (default 48; ring 64) and across (default dome 16, sphere 24, cylinder 4, ring 1).", Required = false)]
            public int? segments { get; set; }

            [ToolParameter("Subdivisions across the shape.", Required = false)]
            public int? rings { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var result = MeshAssetWriter.Run(@params, out string error);
            if (result == null)
                return new ErrorResponse(error, new { toolkitVersion = ToolkitInfo.Version });
            return new SuccessResponse($"{(result.created ? "Created" : "Rebuilt")} {result.shape} mesh at {result.path} ({result.vertices} vertices, {result.triangles} triangles).", result);
        }
    }
}
#endif
