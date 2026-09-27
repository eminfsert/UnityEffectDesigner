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
    /// time is rendered and compared against it. The union of changed pixels over time
    /// gives a screen rectangle; the camera is re-centred on it and moved so it fills
    /// <see cref="FillFraction"/> of the frame. Views whose content touches the frame
    /// edge are zoomed out and measured again.
    /// </summary>
    static class AutoFraming
    {
        const float FillFraction = 0.8f;
        const int ChannelThreshold = 16;
        const int MaxIterations = 3;
        const float ZoomOutOnClip = 1.6f;
        const float MinHalfExtentNdc = 0.03f;

        public static void Refine(CaptureRig rig, EffectSampler sampler, IReadOnlyList<CaptureView> views,
            float[] times, Color background, ViewFraming[] framings, List<string> warnings)
        {
            int size = rig.FrameSize;
            var done = new bool[views.Count];

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                var references = new Color32[views.Count][];
                for (int v = 0; v < views.Count; v++)
                {
                    if (done[v]) continue;
                    AimView(rig, views[v], framings[v]);
                    references[v] = rig.RenderBackgroundOnly(background).GetPixels32();
                }

                var rects = new RectInt?[views.Count];
                foreach (float t in times)
                {
                    sampler.SampleAt(t);
                    for (int v = 0; v < views.Count; v++)
                    {
                        if (done[v]) continue;
                        AimView(rig, views[v], framings[v]);
                        var pixels = rig.Render(background).GetPixels32();
                        if (TryFindChangedRect(pixels, references[v], size, out var rect))
                            rects[v] = rects[v].HasValue ? Union(rects[v].Value, rect) : rect;
                    }
                }

                bool allDone = true;
                for (int v = 0; v < views.Count; v++)
                {
                    if (done[v]) continue;
                    if (!rects[v].HasValue)
                    {
                        warnings.Add($"View '{views[v].Name}': nothing differed from the background at any sampled time; kept bounds-based framing.");
                        done[v] = true;
                        continue;
                    }

                    var r = rects[v].Value;
                    bool touchesEdge = r.xMin <= 0 || r.yMin <= 0 || r.xMax >= size || r.yMax >= size;
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

        static bool TryFindChangedRect(Color32[] frame, Color32[] reference, int size, out RectInt rect)
        {
            int xMin = size, yMin = size, xMax = -1, yMax = -1;
            for (int y = 0; y < size; y++)
            {
                int row = y * size;
                for (int x = 0; x < size; x++)
                {
                    Color32 a = frame[row + x];
                    Color32 b = reference[row + x];
                    if (Mathf.Abs(a.r - b.r) <= ChannelThreshold &&
                        Mathf.Abs(a.g - b.g) <= ChannelThreshold &&
                        Mathf.Abs(a.b - b.b) <= ChannelThreshold)
                        continue;
                    if (x < xMin) xMin = x;
                    if (x > xMax) xMax = x;
                    if (y < yMin) yMin = y;
                    if (y > yMax) yMax = y;
                }
            }

            if (xMax < 0)
            {
                rect = default;
                return false;
            }
            rect = new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
            return true;
        }

        static RectInt Union(RectInt a, RectInt b)
        {
            int xMin = Mathf.Min(a.xMin, b.xMin);
            int yMin = Mathf.Min(a.yMin, b.yMin);
            int xMax = Mathf.Max(a.xMax, b.xMax);
            int yMax = Mathf.Max(a.yMax, b.yMax);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }
    }
}
