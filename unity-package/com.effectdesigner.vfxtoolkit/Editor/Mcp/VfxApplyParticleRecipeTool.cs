#if VFXTOOLKIT_MCP
using EffectDesigner.VFXToolkit.Editor.Recipes;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;

namespace EffectDesigner.VFXToolkit.Editor.Mcp
{
    [McpForUnityTool(
        "vfx_apply_particle_recipe",
        Description =
            "Create or update a hierarchy of Shuriken particle systems from one JSON recipe, in a single call. " +
            "Every ParticleSystem module and renderer property is addressable by its Unity name in snake_case " +
            "(main.start_lifetime, shape.shape_type, texture_sheet_animation.num_tiles_x, renderer.render_mode...), " +
            "plus emission.bursts, sub_emitters, custom_data and renderer.vertex_streams. Curves accept numbers, " +
            "[min,max], [[t,v],...] keys or {ease, from, to}; colors accept #hex, $palette names and HDR " +
            "{color, intensity}. The recipe is validated first: on any error nothing changes and all problems " +
            "are listed with suggestions. Updates are patches (only given keys change); 'reset': true rebuilds " +
            "a system. Target an existing prefab path to edit it in place, a scene object, or omit target to " +
            "create a new root (optionally saved with save_prefab). Use dry_run to validate only.")]
    public static class VfxApplyParticleRecipeTool
    {
        public class Parameters
        {
            [ToolParameter("Array of systems: {name, parent?, position?, rotation?, scale?, reset?, active?, <module>: {...}, renderer: {...}}.")]
            public object[] systems { get; set; }

            [ToolParameter("Existing prefab (Assets/...prefab, edited in place) or scene GameObject to update. Omit to create a new root.", Required = false)]
            public string target { get; set; }

            [ToolParameter("Name of the new root GameObject when target is omitted.", Required = false)]
            public string name { get; set; }

            [ToolParameter("Save the new/updated scene root as a prefab at this path (folders are created).", Required = false)]
            public string save_prefab { get; set; }

            [ToolParameter("Named colors usable as \"$name\" anywhere a color is expected, e.g. {\"core\": \"#FFF4D6\", \"primary\": {\"color\": \"#9B5CFF\", \"intensity\": 2}}.", Required = false)]
            public object palette { get; set; }

            [ToolParameter("Validate and report what would be applied without changing anything. Default false.", Required = false)]
            public bool? dry_run { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var result = ParticleRecipeBuilder.Apply(@params, out var errors);
            if (result == null)
                return new ErrorResponse($"Recipe rejected, nothing was changed ({errors.Count} problem(s)).", new { toolkitVersion = ToolkitInfo.Version, errors });

            string verb = result.dryRun ? "Validated" : "Applied";
            string message = $"{verb} {result.systems.Count} system(s)" +
                             (result.prefab != null ? $", saved to {result.prefab}" : result.root != null ? $" under '{result.root}'" : "") +
                             (result.errors.Count > 0 ? $", with {result.errors.Count} error(s) while applying" : "") + ".";
            return new SuccessResponse(message, result);
        }
    }
}
#endif
