using System;
using System.Collections.Generic;
using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor.Capture
{
    /// <summary>
    /// How far one layer reaches compared with another (the reference layer's silhouette), per time:
    /// "shrapnel flies well past the dome" as numbers. Distances are from the reference silhouette's
    /// centre, in units of its equivalent radius (the radius of a disc of the same area).
    /// </summary>
    public sealed class SpreadStat
    {
        public float time;
        /// <summary>Share of this layer's pixels inside the reference layer's silhouette; -1 when not measured.</summary>
        public float inside = -1f;
        /// <summary>Median, 90th percentile and largest distance of this layer's pixels, in reference radii.</summary>
        public float p50 = -1f;
        public float p90 = -1f;
        public float max = -1f;
    }

    static class SpreadStats
    {
        const int DiffThreshold = 16;
        const int MinPixels = 20;

        public sealed class Silhouette
        {
            public bool[] Mask;
            public int Size;
            public float CenterX, CenterY, Radius;
        }

        static bool Differs(Color32 a, Color32 b) =>
            Mathf.Abs(a.r - b.r) > DiffThreshold || Mathf.Abs(a.g - b.g) > DiffThreshold || Mathf.Abs(a.b - b.b) > DiffThreshold;

        /// <summary>The reference layer's silhouette in a frame of it alone; null if it covers too few pixels.</summary>
        public static Silhouette Shape(Color32[] frame, Color32[] background, int size)
        {
            var mask = new bool[frame.Length];
            double sx = 0, sy = 0;
            int n = 0;
            for (int i = 0; i < frame.Length; i++)
            {
                if (!Differs(frame[i], background[i])) continue;
                mask[i] = true;
                sx += i % size;
                sy += i / size;
                n++;
            }
            if (n < MinPixels)
                return null;
            return new Silhouette { Mask = mask, Size = size, CenterX = (float)(sx / n), CenterY = (float)(sy / n), Radius = Mathf.Sqrt(n / Mathf.PI) };
        }

        public static SpreadStat Measure(Color32[] frame, Color32[] background, Silhouette reference, float time)
        {
            var stat = new SpreadStat { time = time };
            if (reference == null)
                return stat;
            var distances = new List<float>();
            int inside = 0;
            for (int i = 0; i < frame.Length; i++)
            {
                if (!Differs(frame[i], background[i])) continue;
                if (reference.Mask[i]) inside++;
                float dx = i % reference.Size - reference.CenterX, dy = i / reference.Size - reference.CenterY;
                distances.Add(Mathf.Sqrt(dx * dx + dy * dy) / reference.Radius);
            }
            if (distances.Count < MinPixels)
                return stat;
            distances.Sort();
            stat.inside = inside / (float)distances.Count;
            stat.p50 = distances[(int)(0.5f * (distances.Count - 1))];
            stat.p90 = distances[(int)(0.9f * (distances.Count - 1))];
            stat.max = distances[distances.Count - 1];
            return stat;
        }
    }
}
