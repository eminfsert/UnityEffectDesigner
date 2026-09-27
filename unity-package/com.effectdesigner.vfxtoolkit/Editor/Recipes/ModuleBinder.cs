using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace EffectDesigner.VFXToolkit.Editor.Recipes
{
    /// <summary>
    /// Maps recipe keys onto ParticleSystem module / renderer properties by reflection, so
    /// every property Unity exposes is available without hand-written glue. Keys match
    /// property names case-insensitively with underscores ignored (start_lifetime ==
    /// startLifetime). Unknown keys fail with the closest valid names.
    ///
    /// Unity's rotation properties are in radians although the inspector shows degrees;
    /// appending "_deg" to any numeric/curve key (start_rotation_deg, z_deg) converts
    /// degrees to radians.
    /// </summary>
    static class ModuleBinder
    {
        static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> Cache = new Dictionary<Type, Dictionary<string, PropertyInfo>>();

        /// <summary>
        /// The module properties recipes may address. Listed explicitly (not discovered) so a
        /// Unity upgrade that adds a module cannot silently change recipe semantics.
        /// </summary>
        static readonly string[] ModuleNames =
        {
            "main", "emission", "shape", "velocityOverLifetime", "limitVelocityOverLifetime",
            "inheritVelocity", "lifetimeByEmitterSpeed", "forceOverLifetime", "colorOverLifetime",
            "colorBySpeed", "sizeOverLifetime", "sizeBySpeed", "rotationOverLifetime", "rotationBySpeed",
            "externalForces", "noise", "collision", "subEmitters", "textureSheetAnimation", "lights",
            "trails", "customData",
        };

        /// <summary>ParticleSystem module properties (main, emission, shape, ...) keyed by normalized name.</summary>
        public static readonly Dictionary<string, PropertyInfo> Modules = ModuleNames
            .Select(n => typeof(UnityEngine.ParticleSystem).GetProperty(n))
            .Where(p => p != null)
            .ToDictionary(p => RecipeValues.Normalize(p.Name), p => p);

        static Dictionary<string, PropertyInfo> Properties(Type type)
        {
            if (!Cache.TryGetValue(type, out var map))
            {
                map = new Dictionary<string, PropertyInfo>();
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length > 0 || p.GetCustomAttribute<ObsoleteAttribute>() != null)
                        continue;
                    map[RecipeValues.Normalize(p.Name)] = p;
                }
                Cache[type] = map;
            }
            return map;
        }

        /// <summary>
        /// Converts every key of <paramref name="json"/> (except <paramref name="skip"/>) into a setter for an
        /// instance of <paramref name="type"/>. Conversion errors are collected, not thrown.
        /// </summary>
        public static List<Action<object>> Compile(Type type, JObject json, string where, RecipeContext ctx,
            ICollection<string> skip, List<string> errors, Dictionary<string, string> aliases = null)
        {
            var setters = new List<Action<object>>();
            var props = Properties(type);

            foreach (var entry in json.Properties())
            {
                string key = RecipeValues.Normalize(entry.Name);
                if (skip != null && skip.Contains(key))
                    continue;
                if (aliases != null && aliases.TryGetValue(key, out var alias))
                    key = alias;

                string at = $"{where}.{entry.Name}";
                bool degrees = false;
                if (!props.TryGetValue(key, out var prop) && key.EndsWith("deg") && props.TryGetValue(key.Substring(0, key.Length - 3), out prop))
                    degrees = true;
                if (prop == null)
                {
                    errors.Add($"{at}: unknown property. Did you mean: {string.Join(", ", Suggest(key, props.Values.Select(p => p.Name)))}?");
                    continue;
                }
                if (!prop.CanWrite || prop.SetMethod == null || !prop.SetMethod.IsPublic)
                {
                    errors.Add($"{at}: '{prop.Name}' is read-only.");
                    continue;
                }

                object value;
                try
                {
                    value = RecipeValues.Convert(entry.Value, prop.PropertyType, ctx, at);
                    if (degrees)
                        value = ToRadians(value, at);
                }
                catch (RecipeException ex)
                {
                    errors.Add(ex.Message);
                    continue;
                }
                catch (Exception ex)
                {
                    errors.Add($"{at}: {ex.Message}");
                    continue;
                }

                setters.Add(target =>
                {
                    object resolved = value is Deferred d ? d.Resolve() : value;
                    prop.SetValue(target, resolved);
                });
            }
            return setters;
        }

        static object ToRadians(object value, string at)
        {
            const float k = (float)(Math.PI / 180.0);
            switch (value)
            {
                case float f:
                    return f * k;
                case UnityEngine.ParticleSystem.MinMaxCurve c:
                    c.constantMin *= k;
                    c.constantMax *= k;
                    c.curveMultiplier *= k;
                    return c;
                default:
                    throw new RecipeException($"{at}: '_deg' only applies to numbers and curves.");
            }
        }

        public static bool HasProperty(Type type, string normalizedName) => Properties(type).ContainsKey(normalizedName);

        /// <summary>Closest names by edit distance, for "did you mean" messages.</summary>
        public static IEnumerable<string> Suggest(string key, IEnumerable<string> candidates, int count = 4)
        {
            return candidates
                .Select(c => (name: c, d: Distance(key, RecipeValues.Normalize(c)) - (RecipeValues.Normalize(c).Contains(key) ? 3 : 0)))
                .OrderBy(x => x.d)
                .Take(count)
                .Select(x => ToSnake(x.name));
        }

        public static string ToSnake(string camel)
        {
            var chars = new List<char>();
            for (int i = 0; i < camel.Length; i++)
            {
                char c = camel[i];
                if (char.IsUpper(c) && i > 0 && char.IsLower(camel[i - 1]))
                    chars.Add('_');
                chars.Add(char.ToLowerInvariant(c));
            }
            return new string(chars.ToArray());
        }

        static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }
    }
}
