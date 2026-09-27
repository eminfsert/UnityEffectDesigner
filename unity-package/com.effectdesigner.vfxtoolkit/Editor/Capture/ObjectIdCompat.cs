using System.Reflection;
using UnityEditor;

namespace EffectDesigner.VFXToolkit.Editor.Capture
{
    /// <summary>
    /// Resolves the int instance ids that MCP for Unity reports back to objects.
    /// Unity 6.5+ makes EditorUtility.InstanceIDToObject(int) obsolete-as-error in favour
    /// of EntityId, but the method still exists at runtime. Calling it through reflection
    /// compiles on every Unity 6 version and matches how MCP for Unity itself resolves ids.
    /// </summary>
    static class ObjectIdCompat
    {
        static readonly MethodInfo InstanceIdToObject = typeof(EditorUtility).GetMethod(
            "InstanceIDToObject",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(int) },
            null);

        public static UnityEngine.Object FromInstanceId(int instanceId)
        {
            return InstanceIdToObject?.Invoke(null, new object[] { instanceId }) as UnityEngine.Object;
        }
    }
}
