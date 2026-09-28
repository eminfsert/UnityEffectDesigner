using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor.Meshes
{
    /// <summary>Plain mesh arrays, built without touching the engine so shapes can be tested outside Unity.</summary>
    public sealed class MeshData
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uv = new List<Vector2>();
        public readonly List<int> Triangles = new List<int>();

        public int Add(Vector3 v, Vector3 n, Vector2 uv)
        {
            Vertices.Add(v);
            Normals.Add(n);
            Uv.Add(uv);
            return Vertices.Count - 1;
        }

        public void Quad(int a, int b, int c, int d)
        {
            // a-b along u, c-d the next row; counter-clockwise seen from the normal side is Unity's clockwise front.
            Triangles.Add(a); Triangles.Add(c); Triangles.Add(b);
            Triangles.Add(b); Triangles.Add(c); Triangles.Add(d);
        }
    }

    /// <summary>
    /// Procedural shapes for VFX meshes (shells, shockwaves, slashes), with UVs laid out so that
    /// shaders can erode, scroll and color along meaningful directions:
    /// u runs around (or along) the shape and wraps 0..1, v runs across it 0..1.
    /// </summary>
    public static class MeshShapes
    {
        public static readonly string[] Names = { "dome", "sphere", "ring", "cylinder", "arc" };

        public static string Describe(string shape)
        {
            switch (shape)
            {
                case "dome": return "Cap of a sphere sitting on y = 0 (a hemisphere by default), normals outward. u = around (0..1 from +X, counter-clockwise seen from above), v = 0 at the base rim to 1 at the top.";
                case "sphere": return "Full sphere centred at y = radius (resting on y = 0), normals outward. u = around, v = 0 at the bottom to 1 at the top.";
                case "ring": return "Flat annulus on the XZ plane (y = 0), normals up. u = around, v = 0 at the inner edge to 1 at the outer edge.";
                case "cylinder": return "Open tube from y = 0 to y = height (a cone or funnel when top_radius differs), normals outward. u = around, v = 0 at the bottom to 1 at the top.";
                case "arc": return "Flat band along a circular arc in the XY plane (vertical, facing -Z), centred on the arc's midpoint at the origin, thickest in the middle and tapering to the tips. u = 0..1 along the arc, v = 0 inner to 1 outer edge.";
                default: return null;
            }
        }

        public static MeshData Build(string shape, JObject p, out string error)
        {
            error = null;
            float F(string key, float def) => p?[key] != null ? p[key].Value<float>() : def;
            int I(string key, int def) => p?[key] != null ? p[key].Value<int>() : def;
            switch (shape)
            {
                case "dome":
                    return Dome(F("radius", 0.5f), Mathf.Clamp(F("angle", 90f), 1f, 180f), Mathf.Clamp(I("segments", 48), 3, 256), Mathf.Clamp(I("rings", 16), 1, 128), false);
                case "sphere":
                    return Dome(F("radius", 0.5f), 180f, Mathf.Clamp(I("segments", 48), 3, 256), Mathf.Clamp(I("rings", 24), 2, 128), true);
                case "ring":
                    return Ring(F("inner_radius", 0.35f), F("outer_radius", 0.5f), Mathf.Clamp(I("segments", 64), 3, 512), Mathf.Clamp(I("rings", 1), 1, 32));
                case "cylinder":
                    return Cylinder(F("radius", 0.5f), F("top_radius", F("radius", 0.5f)), F("height", 1f), Mathf.Clamp(I("segments", 48), 3, 256), Mathf.Clamp(I("rings", 4), 1, 128));
                case "arc":
                    return Arc(F("radius", 0.5f), Mathf.Clamp(F("arc", 150f), 1f, 360f), F("width", 0.15f), Mathf.Clamp(F("taper", 1f), 0f, 1f), Mathf.Clamp(I("segments", 48), 2, 512));
                default:
                    error = $"Unknown shape '{shape}'. Shapes: {string.Join(", ", Names)}.";
                    return null;
            }
        }

        /// <summary>Cap of a sphere: <paramref name="angle"/> degrees down from the pole (90 = hemisphere, 180 = sphere).</summary>
        public static MeshData Dome(float radius, float angle, int segments, int rings, bool restOnGround)
        {
            var m = new MeshData();
            float capRad = angle * Mathf.Deg2Rad;
            // Base rim height, so the rim sits on y = 0 (a sphere rests on its bottom point).
            float baseY = restOnGround ? -radius : radius * Mathf.Cos(capRad);
            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings;                 // 0 at the base rim, 1 at the top
                float polar = capRad * (1f - v);            // angle from the top
                float y = Mathf.Cos(polar), ring = Mathf.Sin(polar);
                for (int s = 0; s <= segments; s++)
                {
                    float u = s / (float)segments;
                    float az = u * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Cos(az) * ring, y, Mathf.Sin(az) * ring);
                    m.Add(new Vector3(n.x * radius, n.y * radius - baseY, n.z * radius), n, new Vector2(u, v));
                }
            }
            Grid(m, segments, rings);
            return m;
        }

        public static MeshData Ring(float inner, float outer, int segments, int rings)
        {
            var m = new MeshData();
            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings;
                float radius = Mathf.Lerp(inner, outer, v);
                for (int s = 0; s <= segments; s++)
                {
                    float u = s / (float)segments;
                    float az = u * Mathf.PI * 2f;
                    m.Add(new Vector3(Mathf.Cos(az) * radius, 0f, Mathf.Sin(az) * radius), Vector3.up, new Vector2(u, v));
                }
            }
            Grid(m, segments, rings, flip: true);
            return m;
        }

        public static MeshData Cylinder(float bottomRadius, float topRadius, float height, int segments, int rings)
        {
            var m = new MeshData();
            // Outward normal of a cone's side: tilted by the radius change over the height.
            float slope = height > 1e-5f ? (bottomRadius - topRadius) / height : 0f;
            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings;
                float radius = Mathf.Lerp(bottomRadius, topRadius, v);
                for (int s = 0; s <= segments; s++)
                {
                    float u = s / (float)segments;
                    float az = u * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Cos(az), slope, Mathf.Sin(az)).normalized;
                    m.Add(new Vector3(Mathf.Cos(az) * radius, v * height, Mathf.Sin(az) * radius), n, new Vector2(u, v));
                }
            }
            Grid(m, segments, rings);
            return m;
        }

        /// <summary>Band along an arc, thickest in the middle; <paramref name="taper"/> 1 = pointed tips, 0 = constant width.</summary>
        public static MeshData Arc(float radius, float arcDegrees, float width, float taper, int segments)
        {
            var m = new MeshData();
            float half = arcDegrees * 0.5f * Mathf.Deg2Rad;
            for (int s = 0; s <= segments; s++)
            {
                float u = s / (float)segments;
                float a = Mathf.Lerp(-half, half, u) + Mathf.PI * 0.5f;      // symmetric about +Y
                float w = width * Mathf.Lerp(1f, Mathf.Sin(u * Mathf.PI), taper);
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                // Centred on the arc's midpoint so the mesh pivots where the slash is thickest.
                var offset = new Vector3(0f, -radius, 0f);
                m.Add(dir * (radius - w * 0.5f) + offset, Vector3.back, new Vector2(u, 0f));
                m.Add(dir * (radius + w * 0.5f) + offset, Vector3.back, new Vector2(u, 1f));
            }
            for (int s = 0; s < segments; s++)
            {
                int i = s * 2;
                // Facing -Z (toward a default camera).
                m.Triangles.Add(i); m.Triangles.Add(i + 2); m.Triangles.Add(i + 1);
                m.Triangles.Add(i + 2); m.Triangles.Add(i + 3); m.Triangles.Add(i + 1);
            }
            return m;
        }

        /// <summary>Triangles for a (segments+1) x (rings+1) vertex grid, facing along the normals.</summary>
        static void Grid(MeshData m, int segments, int rings, bool flip = false)
        {
            int row = segments + 1;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = r * row + s;
                    if (flip) m.Quad(a + 1, a, a + row + 1, a + row);
                    else m.Quad(a, a + 1, a + row, a + row + 1);
                }
        }

        public static string Summary(MeshData m) =>
            string.Format(CultureInfo.InvariantCulture, "{0} vertices, {1} triangles", m.Vertices.Count, m.Triangles.Count / 3);
    }
}
