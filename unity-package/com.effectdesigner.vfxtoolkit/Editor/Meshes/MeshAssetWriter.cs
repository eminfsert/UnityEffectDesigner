using System;
using System.IO;
using EffectDesigner.VFXToolkit.Editor.Recipes;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EffectDesigner.VFXToolkit.Editor.Meshes
{
    public sealed class MeshResult
    {
        public string toolkitVersion = ToolkitInfo.Version;
        public string path;
        public string shape;
        public bool created;
        public int vertices;
        public int triangles;
        public float[] boundsCenter;
        public float[] boundsSize;
        /// <summary>How the shape's UVs and pivot are laid out, for shaders and recipes.</summary>
        public string layout;
    }

    /// <summary>Builds a <see cref="MeshShapes"/> shape and saves it as a Mesh asset.</summary>
    public static class MeshAssetWriter
    {
        public static MeshResult Run(JObject args, out string error)
        {
            error = null;
            string shape = ((string)args?["shape"])?.Trim().ToLowerInvariant();
            string path = (string)args?["path"];
            if (string.IsNullOrEmpty(shape) || string.IsNullOrEmpty(path))
            {
                error = $"'shape' and 'path' are required. Shapes: {string.Join(", ", MeshShapes.Names)}.";
                return null;
            }
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                error = $"'path' must be a project path under Assets/ ending in .asset, e.g. Assets/VFX/Boom/Meshes/SM_Boom_Dome.asset (got '{path}').";
                return null;
            }

            var data = MeshShapes.Build(shape, args, out error);
            if (data == null)
                return null;

            // Rebuild an existing asset in place so prefabs and particle systems keep their reference.
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created)
                mesh = new Mesh();
            else
                mesh.Clear();

            mesh.name = Path.GetFileNameWithoutExtension(path);
            mesh.indexFormat = data.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.SetUVs(0, data.Uv);
            mesh.SetTriangles(data.Triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            if (created)
            {
                MaterialBuilder.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }
            AssetDatabase.SaveAssets();

            var b = mesh.bounds;
            return new MeshResult
            {
                path = path,
                shape = shape,
                created = created,
                vertices = data.Vertices.Count,
                triangles = data.Triangles.Count / 3,
                boundsCenter = new[] { b.center.x, b.center.y, b.center.z },
                boundsSize = new[] { b.size.x, b.size.y, b.size.z },
                layout = MeshShapes.Describe(shape),
            };
        }
    }
}
