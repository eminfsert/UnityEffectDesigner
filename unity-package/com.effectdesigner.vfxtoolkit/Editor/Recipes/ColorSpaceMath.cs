using System;
using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor.Recipes
{
    /// <summary>
    /// sRGB transfer functions in managed code (Mathf's versions call into the engine), extended
    /// above 1 for HDR values. Intensity is applied in linear light, as Unity's HDR color picker
    /// does: "1 stop" doubles the linear value.
    ///
    /// Color spaces of material colors in a Linear-space project (measured in Unity 6 URP):
    /// a plain Color property stores the sRGB (hex) value and Unity linearizes it for the GPU;
    /// an [HDR] Color property is passed to the GPU as stored, so it must be given the linear
    /// value. Writing the hex to an [HDR] property made every tint lighter and yellower
    /// (#5A3000 rendered as #A07800).
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
        /// <summary>
        /// The value to store in an [HDR] Color property for a color given as sRGB (hex, possibly with
        /// intensity applied by <see cref="WithIntensity"/>): its linear value in a Linear-space project,
        /// unchanged in a Gamma-space one. Alpha is unchanged.
        /// </summary>
        public static Color ForHdrProperty(Color srgb, bool linearColorSpace)
        {
            if (!linearColorSpace) return srgb;
            return new Color(SrgbToLinear(srgb.r), SrgbToLinear(srgb.g), SrgbToLinear(srgb.b), srgb.a);
        }

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
