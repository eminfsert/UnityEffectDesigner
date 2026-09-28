using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor.Recipes
{
    /// <summary>Manual entry point so recipes can be tried without an MCP client.</summary>
    static class RecipeMenu
    {
        [MenuItem("Tools/Effect Designer/Apply Particle Recipe From Clipboard")]
        static void ApplyFromClipboard()
        {
            JObject recipe;
            try
            {
                recipe = JObject.Parse(EditorGUIUtility.systemCopyBuffer);
            }
            catch (JsonException ex)
            {
                Debug.LogError($"[VFX Toolkit] Clipboard does not contain a JSON recipe: {ex.Message}");
                return;
            }

            var result = ParticleRecipeBuilder.Apply(recipe, out var errors);
            if (result == null)
            {
                // One entry per line: MCP read_console only returns the first lines of an entry.
                Debug.LogError($"[VFX Toolkit] Recipe rejected, nothing was changed ({errors.Count} problem(s)).");
                foreach (var e in errors)
                    Debug.LogError($"[VFX Toolkit] {e}");
                return;
            }

            foreach (var warning in result.warnings)
                Debug.LogWarning($"[VFX Toolkit] {warning}");
            foreach (var error in result.errors)
                Debug.LogError($"[VFX Toolkit] {error}");
            Debug.Log($"[VFX Toolkit] {(result.dryRun ? "Validated" : "Applied")} {result.systems.Count} system(s)" +
                      (result.prefab != null ? $", saved to {result.prefab}" : "") + ".");
            foreach (var s in result.systems)
                Debug.Log($"[VFX Toolkit] {s.name}{(s.created ? " (new)" : "")}: {string.Join(", ", s.applied)}");
        }
    }
}
