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
                Debug.LogError($"[VFX Toolkit] Recipe rejected, nothing was changed:\n- {string.Join("\n- ", errors)}");
                return;
            }

            foreach (var warning in result.warnings)
                Debug.LogWarning($"[VFX Toolkit] {warning}");
            foreach (var error in result.errors)
                Debug.LogError($"[VFX Toolkit] {error}");
            Debug.Log($"[VFX Toolkit] {(result.dryRun ? "Validated" : "Applied")} {result.systems.Count} system(s)" +
                      (result.prefab != null ? $", saved to {result.prefab}" : "") + ".\n" +
                      string.Join("\n", result.systems.ConvertAll(s => $"{s.name}{(s.created ? " (new)" : "")}: {string.Join(", ", s.applied)}")));
        }
    }
}
