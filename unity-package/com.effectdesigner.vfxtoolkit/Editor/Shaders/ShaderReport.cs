using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EffectDesigner.VFXToolkit.Editor.Shaders
{
    public sealed class ShaderMessageInfo
    {
        public string message;
        public string details;
        public string file;
        public int line;
        public string platform;
    }

    public sealed class ShaderPropertyInfo
    {
        public string name;
        public string type;
        public bool hdr;
    }

    public sealed class ShaderCheck
    {
        public string path;
        public string name;
        public bool supported;
        public List<ShaderMessageInfo> errors = new List<ShaderMessageInfo>();
        public List<ShaderMessageInfo> warnings = new List<ShaderMessageInfo>();
        public List<ShaderPropertyInfo> properties = new List<ShaderPropertyInfo>();
        /// <summary>Contract properties the shader does not declare.</summary>
        public List<string> missingProperties = new List<string>();
        /// <summary>Contract properties declared with another type, as "name: expected X, found Y".</summary>
        public List<string> propertyTypeMismatches = new List<string>();
    }

    public sealed class ShaderReportResult
    {
        public string toolkitVersion = ToolkitInfo.Version;
        public int checkedShaders;
        public int shadersWithErrors;
        public int contractViolations;
        public List<ShaderCheck> shaders = new List<ShaderCheck>();
        public List<string> warnings = new List<string>();
    }

    /// <summary>
    /// Re-imports shaders and reports compile errors/warnings with file and line, plus each
    /// shader's properties checked against an expected property contract (the names and
    /// types the Systems Architect fixed in manifest.yaml, e.g. _TintColor: Color).
    ///
    /// Accepts shader paths (.shader, .shadergraph), folders, and include files
    /// (.hlsl/.cginc): for an include, every shader under Assets/ that includes it is checked.
    /// </summary>
    public static class ShaderReport
    {
        public static ShaderReportResult Run(JObject args, out string error)
        {
            error = null;
            var paths = ReadStrings(args["paths"]);
            if (paths.Count == 0)
            {
                error = "'paths' is required: shader files (.shader/.shadergraph), include files (.hlsl/.cginc) or folders.";
                return null;
            }
            bool reimport = args["reimport"]?.Value<bool>() ?? true;
            bool includeWarnings = args["include_warnings"]?.Value<bool>() ?? true;
            var contract = ReadContract(args["expected_properties"], out error);
            if (error != null)
                return null;

            var result = new ShaderReportResult();
            var shaderPaths = new List<string>();
            foreach (var path in paths)
                Collect(path, reimport, shaderPaths, result.warnings);

            foreach (var path in shaderPaths.Distinct())
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null)
                {
                    result.warnings.Add($"'{path}' did not import as a shader.");
                    continue;
                }
                var check = Check(shader, path, includeWarnings, contract);
                result.shaders.Add(check);
                if (check.errors.Count > 0) result.shadersWithErrors++;
                result.contractViolations += check.missingProperties.Count + check.propertyTypeMismatches.Count;
            }
            result.checkedShaders = result.shaders.Count;
            if (result.checkedShaders == 0)
                result.warnings.Add("No shaders were found for the given paths.");
            return result;
        }

        static void Collect(string path, bool reimport, List<string> shaderPaths, List<string> warnings)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path))
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { path }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (reimport) Reimport(p);
                    shaderPaths.Add(p);
                }
                return;
            }

            if (!File.Exists(path))
            {
                warnings.Add($"'{path}' does not exist.");
                return;
            }

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".hlsl" || ext == ".cginc" || ext == ".hlslinc")
            {
                if (reimport) Reimport(path);
                string file = Path.GetFileName(path);
                int users = 0;
                foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { "Assets" }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    // Shader Graph files reference includes (Custom Function nodes) by name too.
                    if (!File.Exists(p) || !File.ReadAllText(p).Contains(file))
                        continue;
                    if (reimport) Reimport(p);
                    shaderPaths.Add(p);
                    users++;
                }
                if (users == 0)
                    warnings.Add($"No shader under Assets/ includes '{file}'; it was not compiled.");
                return;
            }

            if (reimport) Reimport(path);
            shaderPaths.Add(path);
        }

        static void Reimport(string path) =>
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        static ShaderCheck Check(Shader shader, string path, bool includeWarnings, Dictionary<string, string> contract)
        {
            var check = new ShaderCheck { path = path, name = shader.name, supported = shader.isSupported };

            foreach (var m in ShaderUtil.GetShaderMessages(shader))
            {
                var info = new ShaderMessageInfo
                {
                    message = m.message,
                    details = string.IsNullOrWhiteSpace(m.messageDetails) ? null : m.messageDetails.Trim(),
                    file = string.IsNullOrEmpty(m.file) ? path : m.file,
                    line = m.line,
                    platform = m.platform.ToString(),
                };
                if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) check.errors.Add(info);
                else if (includeWarnings) check.warnings.Add(info);
            }

            var declared = new Dictionary<string, ShaderPropertyInfo>();
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                var p = new ShaderPropertyInfo
                {
                    name = shader.GetPropertyName(i),
                    type = shader.GetPropertyType(i).ToString(),
                    hdr = (shader.GetPropertyFlags(i) & ShaderPropertyFlags.HDR) != 0,
                };
                check.properties.Add(p);
                declared[p.name] = p;
            }

            foreach (var expected in contract)
            {
                if (!declared.TryGetValue(expected.Key, out var found))
                {
                    check.missingProperties.Add(expected.Key);
                    continue;
                }
                if (expected.Value != null && !TypeMatches(expected.Value, found))
                    check.propertyTypeMismatches.Add($"{expected.Key}: expected {expected.Value}, found {found.type}{(found.hdr ? " (HDR)" : "")}");
            }
            return check;
        }

        /// <summary>Contract types: Color, HDRColor, Float (also accepts Range), Range, Int, Vector, Texture.</summary>
        static bool TypeMatches(string expected, ShaderPropertyInfo found)
        {
            switch (expected.ToLowerInvariant())
            {
                case "hdrcolor":
                case "hdr_color":
                    return found.type == nameof(ShaderPropertyType.Color) && found.hdr;
                case "float":
                    return found.type == nameof(ShaderPropertyType.Float) || found.type == nameof(ShaderPropertyType.Range);
                default:
                    return string.Equals(expected, found.type, StringComparison.OrdinalIgnoreCase);
            }
        }

        static List<string> ReadStrings(JToken token)
        {
            if (token == null) return new List<string>();
            if (token.Type == JTokenType.String)
            {
                string s = token.Value<string>().Trim();
                // Some MCP clients send arrays as JSON strings.
                if (s.StartsWith("[")) return JArray.Parse(s).Select(t => t.Value<string>()).ToList();
                return new List<string> { s };
            }
            return token.Select(t => t.Value<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        /// <summary>Either {"_TintColor": "HDRColor", ...} or ["_TintColor", ...] (names only).</summary>
        static Dictionary<string, string> ReadContract(JToken token, out string error)
        {
            error = null;
            var contract = new Dictionary<string, string>();
            if (token == null) return contract;
            if (token.Type == JTokenType.String)
                token = JToken.Parse(token.Value<string>());

            if (token is JObject obj)
            {
                foreach (var p in obj.Properties())
                    contract[p.Name] = p.Value.Type == JTokenType.Null ? null : p.Value.Value<string>();
            }
            else if (token is JArray arr)
            {
                foreach (var t in arr) contract[t.Value<string>()] = null;
            }
            else
            {
                error = "'expected_properties' must be an object {name: type} or an array of names.";
            }
            return contract;
        }
    }
}
