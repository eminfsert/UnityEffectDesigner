using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor.Capture
{
    /// <summary>Color measurements of the effect's pixels in one frame.</summary>
    public sealed class FrameColorStats
    {
        public float time;
        /// <summary>Share of the frame covered by the effect (pixels differing from the background-only render).</summary>
        public float coverage;
        /// <summary>
        /// Among the effect's bright pixels (HSV value >= 0.5), the share with almost no color
        /// (saturation &lt; 0.15): overexposed HDR or a white tint washing the palette out.
        /// -1 when too few bright pixels to judge.
        /// </summary>
        public float washedOut = -1f;
        /// <summary>Mean HSV saturation of the effect's pixels (0 gray/white, 1 fully saturated).</summary>
        public float saturation;
        /// <summary>Dominant hue in degrees (0 red, 60 yellow, 120 green, 240 blue) over saturated pixels; -1 if none.</summary>
        public float hue;
    }

    /// <summary>
    /// Objective color checks for the critic. Stylized effects read through saturated hues.
    /// Tonemapping keeps overexposed pixels below 255, so "washed out" is measured as bright
    /// pixels that lost their color, not as clipped white. Calibrated on real captures: a gold
    /// effect with a white HDR tint measured 83-100% washed out, the same effect with a normal
    /// material 0%.
    /// </summary>
    static class ColorStats
    {
        const int DiffThreshold = 16;
        const float BrightValue = 0.5f;
        const float ColorlessSaturation = 0.15f;
        const float MinBrightShare = 0.0005f;
        const float HueSaturationMin = 0.25f;

        public static FrameColorStats Measure(Color32[] frame, Color32[] background, float time)
        {
            int effect = 0, bright = 0, colorless = 0;
            double saturationSum = 0, hueX = 0, hueY = 0;

            for (int i = 0; i < frame.Length; i++)
            {
                Color32 a = frame[i];
                Color32 b = background[i];
                if (Mathf.Abs(a.r - b.r) <= DiffThreshold && Mathf.Abs(a.g - b.g) <= DiffThreshold && Mathf.Abs(a.b - b.b) <= DiffThreshold)
                    continue;

                effect++;
                Color.RGBToHSV(a, out float h, out float s, out float v);
                saturationSum += s;
                if (v >= BrightValue)
                {
                    bright++;
                    if (s < ColorlessSaturation)
                        colorless++;
                }
                if (s >= HueSaturationMin)
                {
                    double angle = h * 2.0 * System.Math.PI;
                    hueX += System.Math.Cos(angle) * s;
                    hueY += System.Math.Sin(angle) * s;
                }
            }

            var stats = new FrameColorStats { time = time, coverage = effect / (float)frame.Length, hue = -1f };
            if (effect == 0)
                return stats;

            if (bright >= MinBrightShare * frame.Length)
                stats.washedOut = colorless / (float)bright;
            stats.saturation = (float)(saturationSum / effect);
            if (hueX != 0 || hueY != 0)
            {
                double degrees = System.Math.Atan2(hueY, hueX) * 180.0 / System.Math.PI;
                stats.hue = (float)((degrees + 360.0) % 360.0);
            }
            return stats;
        }
    }
}
