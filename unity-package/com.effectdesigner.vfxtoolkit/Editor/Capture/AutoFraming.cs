using System.Collections.Generic;
using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor.Capture
{
    /// <summary>Camera placement for one view.</summary>
    public struct ViewFraming
    {
        public Vector3 Center;
        public float Distance;
        /// <summary>Effect size in meters, used for clip planes only.</summary>
        public float DepthRadius;
    }

    /// <summary>
    /// Frames each view on what is actually visible in the rendered images, not on 3D
    /// bounds. 3D bounds over-estimate (invisible or fully faded particles, stale renderer
    /// bounds, sphere-around-box padding), which leaves the effect small and off-centre.
    ///
    /// For every view, a background-only reference frame is rendered (so vignette, fog
    /// and other post-processing are not mistaken for the effect), then every sampled
    /// time is rendered and compared against it. Changed pixels are accumulated into
    /// per-column and per-row histograms over all times, and the screen rectangle is
    /// taken between the <see cref="TrimFraction"/> and 1 - TrimFraction quantiles on
    /// each axis. Trimming matters: a few stray particles flung far away would otherwise
    /// stretch the rectangle across the frame and leave the dense body of the effect
    /// small and off-centre. The camera is re-centred on the rectangle and moved so it
    /// fills <see cref="FillFraction"/> of the frame. Views whose content is cut by the
    /// frame edge are zoomed out and measured again.
    /// </summary>
    static class AutoFraming
    {
        const float FillFraction = 0.8f;
        const int ChannelThreshold = 16;
        const int MaxIterations = 3;
        const float ZoomOutOnClip = 1.6f;
        const float MinHalfExtentNdc = 0.03f;
        /// <summary>Share of changed pixels ignored on each side of each axis.</summary>
        const float TrimFraction = 0.02f;
        /// <summary>Share of changed pixels on the frame border above which the effect counts as cut off.</summary>
        const float EdgeFraction = 0.01f;

        public static void Refine(CaptureRig rig, EffectSampler sampler, IReadOnlyList<CaptureView> views,
            float[] times, Color background, ViewFraming[] framings, List<string> warnings, bool[] fixedViews = null)
        {
            int size = rig.FrameSize;
            var done = fixedViews != null ? (bool[])fixedViews.Clone() : new bool[views.Count];

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                var references = new Color32[views.Count][];
                for (int v = 0; v < views.Count; v++)
                {
                    if (done[v]) continue;
                    AimView(rig, views[v], framings[v]);
                    references[v] = rig.RenderBackgroundOnly(background).GetPixels32();
                }

                var coverage = new Coverage[views.Count];
                foreach (float t in times)
                {
                    sampler.SampleAt(t);
                    for (int v = 0; v < views.Count; v++)
                    {
                        if (done[v]) continue;
                        coverage[v] ??= new Coverage(size);
                        AimView(rig, views[v], framings[v]);
                        coverage[v].Add(rig.Render(background).GetPixels32(), references[v]);
                    }
                }

                bool allDone = true;
                for (int v = 0; v < views.Count; v++)
                {
                    if (done[v]) continue;
                    if (coverage[v] == null || coverage[v].Total == 0)
                    {
                        warnings.Add($"View '{views[v].Name}': nothing differed from the background at any sampled time; kept bounds-based framing.");
                        done[v] = true;
                        continue;
                    }

                    var r = coverage[v].TrimmedRect(TrimFraction);
                    bool touchesEdge = coverage[v].EdgeShare >= EdgeFraction;
                    if (touchesEdge && iteration < MaxIterations - 1)
                    {
                        framings[v].Distance *= ZoomOutOnClip;
                        allDone = false;
                        continue;
                    }
                    if (touchesEdge)
                        warnings.Add($"View '{views[v].Name}': the effect still reaches the frame edge after zooming out; parts may be cropped.");

                    AimView(rig, views[v], framings[v]);
                    framings[v] = Recentre(framings[v], r, size, rig.CameraTransform, rig.TanHalfFov);
                    done[v] = true;
                }

                if (allDone)
                    break;
            }
        }

        static void AimView(CaptureRig rig, CaptureView view, ViewFraming f) => rig.Aim(f.Center, f.Distance, f.DepthRadius, view);

        /// <summary>Moves the look-at point onto the rectangle's centre and sets the distance so it fills the frame.</summary>
        static ViewFraming Recentre(ViewFraming f, RectInt r, int size, Transform camera, float tanHalfFov)
        {
            float u0 = r.xMin / (float)size * 2f - 1f;
            float u1 = r.xMax / (float)size * 2f - 1f;
            float v0 = r.yMin / (float)size * 2f - 1f;
            float v1 = r.yMax / (float)size * 2f - 1f;
            float cx = (u0 + u1) * 0.5f;
            float cy = (v0 + v1) * 0.5f;
            float halfExtent = Mathf.Max(MinHalfExtentNdc, Mathf.Max(u1 - u0, v1 - v0) * 0.5f);

            // World half-size of the view at the look-at depth (square frame, aspect 1).
            float halfWorld = f.Distance * tanHalfFov;
            f.Center += camera.right * (cx * halfWorld) + camera.up * (cy * halfWorld);
            f.Distance = halfExtent * halfWorld / FillFraction / tanHalfFov;
            return f;
        }

        /// <summary>Changed-pixel histograms for one view, accumulated over all sampled times.</summary>
        sealed class Coverage
        {
            readonly int _size;
            readonly long[] _columns;
            readonly long[] _rows;
            long _edge;

            public long Total { get; private set; }
            public float EdgeShare => Total > 0 ? _edge / (float)Total : 0f;

            public Coverage(int size)
            {
                _size = size;
                _columns = new long[size];
                _rows = new long[size];
            }

            public void Add(Color32[] frame, Color32[] reference)
            {
                int last = _size - 1;
                for (int y = 0; y < _size; y++)
                {
                    int row = y * _size;
                    for (int x = 0; x < _size; x++)
                    {
                        Color32 a = frame[row + x];
                        Color32 b = reference[row + x];
                        if (Mathf.Abs(a.r - b.r) <= ChannelThreshold &&
                            Mathf.Abs(a.g - b.g) <= ChannelThreshold &&
                            Mathf.Abs(a.b - b.b) <= ChannelThreshold)
                            continue;
                        _columns[x]++;
                        _rows[y]++;
                        Total++;
                        if (x == 0 || y == 0 || x == last || y == last)
                            _edge++;
                    }
                }
            }

            /// <summary>Rectangle between the trim and 1 - trim quantiles on each axis.</summary>
            public RectInt TrimmedRect(float trim)
            {
                QuantileRange(_columns, trim, out int xMin, out int xMax);
                QuantileRange(_rows, trim, out int yMin, out int yMax);
                return new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
            }

            void QuantileRange(long[] histogram, float trim, out int min, out int max)
            {
                long cut = (long)(Total * trim);
                long sum = 0;
                min = 0;
                for (int i = 0; i < histogram.Length; i++)
                {
                    sum += histogram[i];
                    if (sum > cut) { min = i; break; }
                }
                sum = 0;
                max = histogram.Length - 1;
                for (int i = histogram.Length - 1; i >= 0; i--)
                {
                    sum += histogram[i];
                    if (sum > cut) { max = i; break; }
                }
                if (max < min)
                    max = min;
            }
        }
    }
}
