using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor
{
    /// <summary>
    /// Per-project settings shared by the toolkit and the agents, stored in
    /// ProjectSettings/EffectDesigner.json (commit it with the project).
    ///
    /// volume_profile: the game scene's VolumeProfile. Captures use it by default so colors are
    /// judged under the game's tonemapping/bloom/color adjustments. Scene volumes never reach the
    /// capture's preview scene, so without it only the pipeline's default profiles apply.
    /// </summary>
    public sealed class EffectDesignerSettings
    {
        const string RelativePath = "ProjectSettings/EffectDesigner.json";

        [JsonProperty("volume_profile")]
        public string VolumeProfile;

        public static string FilePath => Path.Combine(Path.GetDirectoryName(Application.dataPath), RelativePath);

        public static EffectDesignerSettings Load()
        {
            try
            {
                return File.Exists(FilePath)
                    ? JsonConvert.DeserializeObject<EffectDesignerSettings>(File.ReadAllText(FilePath)) ?? new EffectDesignerSettings()
                    : new EffectDesignerSettings();
            }
            catch (JsonException ex)
            {
                Debug.LogWarning($"[VFX Toolkit] Ignoring unreadable {RelativePath}: {ex.Message}");
                return new EffectDesignerSettings();
            }
        }

        public void Save() => File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));

        const string UseProfileMenu = "Assets/Effect Designer/Use As Capture Volume Profile";

        [MenuItem(UseProfileMenu)]
        static void UseSelectedProfile()
        {
            var settings = Load();
            settings.VolumeProfile = AssetDatabase.GetAssetPath(Selection.activeObject);
            settings.Save();
            Debug.Log($"[VFX Toolkit] Captures now render under '{settings.VolumeProfile}' by default ({RelativePath}).");
        }

        [MenuItem(UseProfileMenu, true)]
        static bool CanUseSelectedProfile() =>
            Selection.activeObject != null && Selection.activeObject.GetType().Name == "VolumeProfile";
    }
}
