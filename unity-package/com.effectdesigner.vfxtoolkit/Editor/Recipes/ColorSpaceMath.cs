using System;
using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor.Recipes
{
    /// <summary>
    /// sRGB transfer functions in managed code (Mathf's versions call into the engine), extended
    /// above 1 for HDR values. Used to apply HDR intensity in linear light, as Unity's HDR color
    /// picker does: in a Linear-space project the GPU sees the linear value, so "1 stop" must
    /// double the linear value, not the gamma-encoded one (which would be ~x2.3 in linear).
    /// </summary>
    public static class ColorSpaceMath
    {
        public static float SrgbToLinear(float c)
        {
            if (c <= 0.04045f) return c / 12.92f;
            return (float)Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        public static float LinearToSrgb(float l)
        {
            if (l <= 0.0031308f) return l * 12.92f;
            return (float)(1.055 * Math.Pow(l, 1.0 / 2.4) - 0.055);
        }

        /// <summary>
        /// A gamma-encoded color (as materials store it) whose linear value is the input's linear
        /// value times 2^stops. Alpha is unchanged.
        /// </summary>
        public static Color WithIntensity(Color gamma, float stops)
        {
            if (stops == 0f) return gamma;
            float k = (float)Math.Pow(2.0, stops);
            return new Color(
                LinearToSrgb(SrgbToLinear(gamma.r) * k),
                LinearToSrgb(SrgbToLinear(gamma.g) * k),
                LinearToSrgb(SrgbToLinear(gamma.b) * k),
                gamma.a);
        }
    }
}
