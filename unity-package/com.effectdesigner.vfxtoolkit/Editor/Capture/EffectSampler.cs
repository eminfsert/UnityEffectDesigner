using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace EffectDesigner.VFXToolkit.Editor.Capture
{
    /// <summary>
    /// Puts an effect hierarchy into the exact visual state it has at a given time,
    /// deterministically and in edit mode, and asks every <see cref="IVfxTimeSampleable"/>
    /// to sample itself.
    ///
    /// The effect is played forward frame by frame at 60 fps, the way the game plays it,
    /// rather than jumped to each time with one Simulate(t, restart) call. A single jump
    /// proved unreliable in practice: short times (one frame) emitted nothing, so bursts at
    /// t = 0 never showed, and death sub-emitters never fired. Stepping forward from a
    /// seeded restart fixes both, and sampling times in ascending order costs one pass
    /// over the timeline instead of one simulation per time.
    /// </summary>
    public sealed class EffectSampler
    {
        /// <summary>Shader globals that let VFX Toolkit shaders use capture time instead of _Time.</summary>
        public static readonly int CaptureTimeId = Shader.PropertyToID("_VFXToolkitTime");
        public static readonly int CaptureActiveId = Shader.PropertyToID("_VFXToolkitCapture");

        /// <summary>Simulation frame length. Time 0 is shown after the first frame.</summary>
        public const float FrameStep = 1f / 60f;

        /// <summary>Simulated time of the current state; negative before the first sample.</summary>
        float _current = -1f;

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

            float target = Mathf.Max(time, FrameStep);
            if (_current < 0f || target < _current - FrameStep * 0.5f)
                Restart();

            // Children (including sub-emitters) are simulated by their root system.
            while (_current + FrameStep * 0.5f < target)
            {
                foreach (var ps in _rootParticleSystems)
                    ps.Simulate(FrameStep, true, false, false);
                foreach (var vfx in _visualEffects)
                    vfx.Simulate(FrameStep, 1);
                _current += FrameStep;
            }
        }

        /// <summary>Back to t = 0 with the fixed seeds, so every pass over the timeline is identical.</summary>
        void Restart()
        {
            foreach (var ps in _rootParticleSystems)
                ps.Simulate(0f, true, true, false);
            foreach (var vfx in _visualEffects)
                vfx.Reinit();
            _current = 0f;
        }

        /// <summary>Label per particle system / visual effect, in <see cref="CurrentCountsPerSystem"/> order.</summary>
        public List<string> SystemLabels()
        {
            var labels = new List<string>();
            foreach (var ps in _allParticleSystems) labels.Add(Unique(labels, ps.name));
            foreach (var vfx in _visualEffects) labels.Add(Unique(labels, vfx.name + " (VFX Graph)"));
            return labels;
        }

        static string Unique(List<string> existing, string name)
        {
            string label = name;
            for (int i = 2; existing.Contains(label); i++)
                label = $"{name} #{i}";
            return label;
        }

        /// <summary>(parent index, sub-emitter index) pairs into <see cref="SystemLabels"/>.</summary>
        public List<(int parent, int child)> SubEmitterLinks()
        {
            var links = new List<(int, int)>();
            for (int p = 0; p < _allParticleSystems.Length; p++)
            {
                var sub = _allParticleSystems[p].subEmitters;
                if (!sub.enabled) continue;
                for (int i = 0; i < sub.subEmittersCount; i++)
                {
                    int c = System.Array.IndexOf(_allParticleSystems, sub.GetSubEmitterSystem(i));
                    if (c >= 0) links.Add((p, c));
                }
            }
            return links;
        }

        /// <summary>Alive particles per system right now, in <see cref="SystemLabels"/> order.</summary>
        public List<int> CurrentCountsPerSystem()
        {
            var counts = new List<int>();
            foreach (var ps in _allParticleSystems) counts.Add(ps.particleCount);
            foreach (var vfx in _visualEffects) counts.Add(vfx.aliveParticleCount);
            return counts;
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
