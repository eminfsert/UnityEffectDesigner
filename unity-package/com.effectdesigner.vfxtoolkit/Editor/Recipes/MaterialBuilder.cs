using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EffectDesigner.VFXToolkit.Editor.Recipes
{
    /// <summary>
    /// Creates or updates a material asset from an inline recipe object, so a particle recipe
    /// can define its materials in the same call:
    ///
    ///   "material": { "path": "Assets/VFX/X/M_Spark.mat", "shader": "EffectDesigner/Particles/Stylized Unlit",
    ///                 "blend": "additive", "properties": { "_TintColor": {"color": "#FFC247", "intensity": 2} },
    ///                 "keywords": { "_SOFTPARTICLES_ON": true } }
    ///
    /// An existing material at "path" is patched (only the given properties change); "shader" is then
    /// optional. Unlike particle colors, material colors keep their HDR intensity.
    /// </summary>
    static class MaterialBuilder
    {
        static readonly Dictionary<string, (BlendMode src, BlendMode dst)> BlendPresets = new Dictionary<string, (BlendMode, BlendMode)>
        {
            { "additive", (BlendMode.SrcAlpha, BlendMode.One) },
            { "alpha", (BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha) },
            { "premultiplied", (BlendMode.One, BlendMode.OneMinusSrcAlpha) },
            { "multiply", (BlendMode.DstColor, BlendMode.Zero) },
            { "soft_additive", (BlendMode.OneMinusDstColor, BlendMode.One) },
        };

        /// <summary>Checks the shape of the recipe without touching assets, so errors surface before anything changes.</summary>
        public static void Validate(JObject obj, string where)
        {
            string path = obj["path"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/") || !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                throw new RecipeException($"{where}.path: an inline material needs \"path\": \"Assets/.../Name.mat\".");
            if (obj["shader"] == null && !File.Exists(path))
                throw new RecipeException($"{where}.shader: the material at '{path}' does not exist yet, so \"shader\" is required.");
            string blend = obj["blend"]?.Value<string>();
            if (blend != null && !BlendPresets.ContainsKey(RecipeValues.Normalize(blend).Replace("softadditive", "soft_additive")))
                throw new RecipeException($"{where}.blend: '{blend}' is not a preset. Valid: {string.Join(", ", BlendPresets.Keys)}.");
            if (obj["properties"] != null && !(obj["properties"] is JObject))
                throw new RecipeException($"{where}.properties: expected an object of shader property values.");
        }

        public static Material Build(JObject obj, RecipeContext ctx, string where)
        {
            string path = obj["path"].Value<string>();
            string shaderName = obj["shader"]?.Value<string>();
            Shader shader = null;
            if (shaderName != null)
            {
                shader = Shader.Find(shaderName) ?? AssetDatabase.LoadAssetAtPath<Shader>(shaderName);
                if (shader == null)
                    throw new RecipeException($"{where}.shader: no shader named or at '{shaderName}'.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null && material.shader != shader)
            {
                material.shader = shader;
            }

            string blend = obj["blend"]?.Value<string>();
            if (blend != null)
            {
                var (src, dst) = BlendPresets[RecipeValues.Normalize(blend).Replace("softadditive", "soft_additive")];
                if (!material.HasProperty("_SrcBlend") || !material.HasProperty("_DstBlend"))
                    throw new RecipeException($"{where}.blend: shader '{material.shader.name}' has no _SrcBlend/_DstBlend properties.");
                material.SetFloat("_SrcBlend", (float)src);
                material.SetFloat("_DstBlend", (float)dst);
            }

            if (obj["properties"] is JObject props)
            {
                foreach (var p in props.Properties())
                    SetProperty(material, p.Name, p.Value, ctx, $"{where}.properties.{p.Name}");
            }

            if (obj["keywords"] is JObject keywords)
            {
                foreach (var k in keywords.Properties())
                {
                    if (k.Value.Value<bool>()) material.EnableKeyword(k.Name);
                    else material.DisableKeyword(k.Name);
                }
            }
            else if (obj["keywords"] is JArray enabled)
            {
                foreach (var k in enabled) material.EnableKeyword(k.Value<string>());
            }

            if (obj["render_queue"] != null)
                material.renderQueue = obj["render_queue"].Value<int>();

            EditorUtility.SetDirty(material);
#if UNITY_2021_2_OR_NEWER
            AssetDatabase.SaveAssetIfDirty(material);
#else
            AssetDatabase.SaveAssets();
#endif
            return material;
        }

        static void SetProperty(Material material, string name, JToken value, RecipeContext ctx, string where)
        {
            var shader = material.shader;
            int index = shader.FindPropertyIndex(name);
            if (index < 0)
            {
                var names = Enumerable.Range(0, shader.GetPropertyCount()).Select(shader.GetPropertyName);
                throw new RecipeException($"{where}: shader '{shader.name}' has no property '{name}'. Did you mean: {string.Join(", ", names.OrderBy(n => Distance(name, n)).Take(4))}?");
            }

            switch (shader.GetPropertyType(index))
            {
                case ShaderPropertyType.Color:
                    var color = RecipeValues.ToColor(value, ctx, where);
                    // [HDR] colors reach the GPU as stored (linear); plain colors are linearized by Unity.
                    if ((shader.GetPropertyFlags(index) & ShaderPropertyFlags.HDR) != 0)
                        color = ColorSpaceMath.ForHdrProperty(color, QualitySettings.activeColorSpace == ColorSpace.Linear);
                    material.SetColor(name, color);
                    break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    material.SetFloat(name, value.Type == JTokenType.Boolean ? (value.Value<bool>() ? 1f : 0f) : value.Value<float>());
                    break;
                case ShaderPropertyType.Int:
                    material.SetInteger(name, value.Value<int>());
                    break;
                case ShaderPropertyType.Vector:
                    material.SetVector(name, (Vector4)RecipeValues.Convert(value, typeof(Vector4), ctx, where));
                    break;
                case ShaderPropertyType.Texture:
                    // "path" | null | {"texture": "path" (optional), "tiling": [x, y], "offset": [x, y]}
                    if (value is JObject tex)
                    {
                        if (tex["texture"] != null)
                            material.SetTexture(name, tex["texture"].Type == JTokenType.Null ? null : (Texture)RecipeValues.Convert(tex["texture"], typeof(Texture), ctx, where + ".texture"));
                        if (tex["tiling"] != null)
                            material.SetTextureScale(name, (Vector2)RecipeValues.Convert(tex["tiling"], typeof(Vector2), ctx, where + ".tiling"));
                        if (tex["offset"] != null)
                            material.SetTextureOffset(name, (Vector2)RecipeValues.Convert(tex["offset"], typeof(Vector2), ctx, where + ".offset"));
                        break;
                    }
                    material.SetTexture(name, value.Type == JTokenType.Null ? null : (Texture)RecipeValues.Convert(value, typeof(Texture), ctx, where));
                    break;
            }
        }

        static int Distance(string a, string b)
        {
            a = a.ToLowerInvariant();
            b = b.ToLowerInvariant();
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        public static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
