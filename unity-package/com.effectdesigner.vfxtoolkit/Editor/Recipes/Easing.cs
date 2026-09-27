using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EffectDesigner.VFXToolkit.Editor.Recipes
{
    /// <summary>
    /// Named timing curves, so recipes can say "ease_out_expo from 1 to 0" instead of
    /// hand-placing keys. Shapes that matter for stylized VFX (spike, pop) are included.
    /// </summary>
    public static class Easing
    {
        const int Samples = 16;

        static readonly Dictionary<string, Func<float, float>> Functions = new Dictionary<string, Func<float, float>>(StringComparer.OrdinalIgnoreCase)
        {
            { "linear", t => t },
            { "ease_in_quad", t => t * t },
            { "ease_out_quad", t => 1f - (1f - t) * (1f - t) },
            { "ease_in_out_quad", t => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f },
            { "ease_in_cubic", t => t * t * t },
            { "ease_out_cubic", t => 1f - Mathf.Pow(1f - t, 3f) },
            { "ease_in_out_cubic", t => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f },
            { "ease_in_expo", t => t <= 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f) },
            { "ease_out_expo", t => t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t) },
            { "ease_in_back", t => 2.70158f * t * t * t - 1.70158f * t * t },
            { "ease_out_back", t => 1f + 2.70158f * Mathf.Pow(t - 1f, 3f) + 1.70158f * Mathf.Pow(t - 1f, 2f) },
            // 0 -> 1 almost instantly, then decays: flashes, impacts.
            { "spike", t => t < 0.08f ? t / 0.08f : Mathf.Pow(1f - (t - 0.08f) / 0.92f, 2.5f) },
            // Overshoots to 1.2 then settles and fades at the end: stylized "pop".
            { "pop", t => t < 0.15f ? 1.2f * (1f - Mathf.Pow(1f - t / 0.15f, 3f))
                        : t < 0.3f ? Mathf.Lerp(1.2f, 1f, (t - 0.15f) / 0.15f)
                        : t < 0.75f ? 1f
                        : 1f - Mathf.Pow((t - 0.75f) / 0.25f, 2f) },
            // Fade in over the first 20%, hold, fade out over the last 30%.
            { "fade_in_out", t => t < 0.2f ? t / 0.2f : t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f },
        };

        public static IEnumerable<string> Names => Functions.Keys;

        public static bool Exists(string name) => name != null && Functions.ContainsKey(name);

        /// <summary>Curve over t in [0,1] with value from + (to - from) * ease(t).</summary>
        public static AnimationCurve Curve(string name, float from, float to)
        {
            var f = Functions[name];
            var keys = new Keyframe[Samples + 1];
            for (int i = 0; i <= Samples; i++)
            {
                float t = i / (float)Samples;
                keys[i] = new Keyframe(t, from + (to - from) * f(t));
            }
            return Smooth(new AnimationCurve(keys));
        }

        /// <summary>Recomputes tangents so hand-written keys interpolate smoothly without overshoot.</summary>
        public static AnimationCurve Smooth(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            return curve;
        }
    }
}
