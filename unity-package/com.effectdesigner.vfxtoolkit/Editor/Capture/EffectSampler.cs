using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace EffectDesigner.VFXToolkit.Editor.Capture
{
    /// <summary>
    /// Puts an effect hierarchy into the exact visual state it has at a given time,
    /// deterministically and in edit mode: particle systems are re-simulated from zero
    /// with fixed seeds, VisualEffects are reinitialized and stepped, and every
    /// <see cref="IVfxTimeSampleable"/> is asked to sample itself.
    /// </summary>
    public sealed class EffectSampler
    {
        /// <summary>Shader globals that let VFX Toolkit shaders use capture time instead of _Time.</summary>
        public static readonly int CaptureTimeId = Shader.PropertyToID("_VFXToolkitTime");
        public static readonly int CaptureActiveId = Shader.PropertyToID("_VFXToolkitCapture");

        const float VfxStep = 1f / 60f;

        /// <summary>
        /// Time 0 is shown as the first rendered frame. Simulating exactly 0 s emits nothing,
        /// so bursts at t = 0 (impact flashes) would never appear in the first column.
        /// </summary>
        public const float FirstFrame = 1f / 60f;

        readonly List<ParticleSystem> _rootParticleSystems = new List<ParticleSystem>();
        readonly ParticleSystem[] _allParticleSystems;
        readonly VisualEffect[] _visualEffects;
        readonly List<IVfxTimeSampleable> _sampleables = new List<IVfxTimeSampleable>();
        readonly Renderer[] _renderers;

        public int ParticleSystemCount => _allParticleSystems.Length;
        public int VisualEffectCount => _visualEffects.Length;
        public int SampleableCount => _sampleables.Count;

        public EffectSampler(GameObject root, int seed)
        {
            _allParticleSystems = root.GetComponentsInChildren<ParticleSystem>(true);
            _visualEffects = root.GetComponentsInChildren<VisualEffect>(true);
            _renderers = root.GetComponentsInChildren<Renderer>(true);

            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is IVfxTimeSampleable sampleable)
                    _sampleables.Add(sampleable);
            }

            for (int i = 0; i < _allParticleSystems.Length; i++)
            {
                var ps = _allParticleSystems[i];
                // The seed can only be changed while the system is stopped.
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.useAutoRandomSeed = false;
                ps.randomSeed = unchecked((uint)(seed + i * 7919));

                if (!HasParticleSystemAncestor(ps.transform, root.transform))
                    _rootParticleSystems.Add(ps);
            }

            foreach (var vfx in _visualEffects)
            {
                vfx.resetSeedOnPlay = false;
                vfx.startSeed = unchecked((uint)seed);
            }
        }

        static bool HasParticleSystemAncestor(Transform t, Transform stopAt)
        {
            for (var p = t.parent; p != null; p = p.parent)
            {
                if (p.GetComponent<ParticleSystem>() != null)
                    return true;
                if (p == stopAt)
                    break;
            }
            return false;
        }

        /// <summary>Sets the whole effect to its state at <paramref name="time"/> seconds.</summary>
        public void SampleAt(float time)
        {
            Shader.SetGlobalFloat(CaptureTimeId, time);
            Shader.SetGlobalFloat(CaptureActiveId, 1f);

            foreach (var sampleable in _sampleables)
                sampleable.SampleAt(time);

            float simulated = Mathf.Max(time, FirstFrame);

            // Children (including sub-emitters) are simulated by their root system.
            foreach (var ps in _rootParticleSystems)
                ps.Simulate(simulated, true, true, true);

            foreach (var vfx in _visualEffects)
            {
                vfx.Reinit();
                uint steps = (uint)Mathf.Max(1, Mathf.RoundToInt(simulated / VfxStep));
                if (steps > 0)
                    vfx.Simulate(VfxStep, steps);
            }
        }

        public int CurrentParticleCount()
        {
            int count = 0;
            foreach (var ps in _allParticleSystems)
                count += ps.particleCount;
            foreach (var vfx in _visualEffects)
                count += vfx.aliveParticleCount;
            return count;
        }

        /// <summary>
        /// World bounds of everything visible right now, or false if nothing is.
        /// Particles are measured from their actual positions and sizes, because
        /// ParticleSystemRenderer.bounds is only refreshed when the system renders.
        /// </summary>
        public bool TryGetVisibleBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            foreach (var ps in _allParticleSystems)
            {
                if (!ps.gameObject.activeInHierarchy || ps.particleCount == 0)
                    continue;
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer == null || !renderer.enabled)
                    continue;

                if (_particleBuffer.Length < ps.particleCount)
                    _particleBuffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(ps.particleCount)];
                int count = ps.GetParticles(_particleBuffer);
                var toWorld = SimulationToWorld(ps);
                for (int i = 0; i < count; i++)
                {
                    Vector3 size = _particleBuffer[i].GetCurrentSize3D(ps);
                    float extent = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.5f;
                    var b = new Bounds(toWorld.MultiplyPoint3x4(_particleBuffer[i].position), Vector3.one * (extent * 2f));
                    Encapsulate(ref bounds, ref any, b);
                }
            }

            foreach (var r in _renderers)
            {
                if (r == null || r is ParticleSystemRenderer || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                var b = r.bounds;
                if (b.size.sqrMagnitude > 0f)
                    Encapsulate(ref bounds, ref any, b);
            }
            return any;
        }

        ParticleSystem.Particle[] _particleBuffer = new ParticleSystem.Particle[256];

        static Matrix4x4 SimulationToWorld(ParticleSystem ps)
        {
            var main = ps.main;
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.World:
                    return Matrix4x4.identity;
                case ParticleSystemSimulationSpace.Custom:
                    return main.customSimulationSpace != null
                        ? main.customSimulationSpace.localToWorldMatrix
                        : Matrix4x4.identity;
                default:
                    return ps.transform.localToWorldMatrix;
            }
        }

        static void Encapsulate(ref Bounds bounds, ref bool any, Bounds b)
        {
            if (!any)
            {
                bounds = b;
                any = true;
            }
            else
            {
                bounds.Encapsulate(b);
            }
        }

        public static void ClearCaptureGlobals()
        {
            Shader.SetGlobalFloat(CaptureActiveId, 0f);
            Shader.SetGlobalFloat(CaptureTimeId, 0f);
        }
    }
}
