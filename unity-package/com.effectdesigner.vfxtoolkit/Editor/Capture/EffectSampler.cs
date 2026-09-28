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

        /// <summary>Simulation frame length.</summary>
        public const float FrameStep = 1f / 60f;

        /// <summary>
        /// Simulated frames needed to show time <paramref name="time"/>: t = 0 is the first rendered
        /// frame (one step simulated, as in the game), and every 1/60 s after it adds a frame.
        /// Times rounding to the same frame show the same state.
        /// </summary>
        public static int FrameIndex(float time) => Mathf.Max(0, Mathf.RoundToInt(time / FrameStep)) + 1;

        /// <summary>Frames simulated for the current state; negative before the first sample.</summary>
        int _frames = -1;

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
            _maxParticleSize = new float[_allParticleSystems.Length];

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

            int target = FrameIndex(time);
            if (_frames < 0 || target < _frames)
                Restart();

            // Children (including sub-emitters) are simulated by their root system.
            while (_frames < target)
            {
                foreach (var ps in _rootParticleSystems)
                    ps.Simulate(FrameStep, true, false, false);
                foreach (var vfx in _visualEffects)
                    vfx.Simulate(FrameStep, 1);
                _frames++;
            }
        }

        /// <summary>Back to t = 0 with the fixed seeds, so every pass over the timeline is identical.</summary>
        void Restart()
        {
            foreach (var ps in _rootParticleSystems)
                ps.Simulate(0f, true, true, false);
            foreach (var vfx in _visualEffects)
                vfx.Reinit();
            _frames = 0;
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

        /// <summary>
        /// The renderer of each system in <see cref="SystemLabels"/> order, or null when it has none or
        /// it is disabled (nothing of that system is ever drawn).
        /// </summary>
        public List<Renderer> SystemRenderers()
        {
            var renderers = new List<Renderer>();
            foreach (var ps in _allParticleSystems) renderers.Add(Drawn(ps.GetComponent<ParticleSystemRenderer>()));
            foreach (var vfx in _visualEffects) renderers.Add(Drawn(vfx.GetComponent<Renderer>()));
            return renderers;
        }

        static Renderer Drawn(Renderer r) => r != null && r.enabled && r.gameObject.activeInHierarchy ? r : null;

        /// <summary>
        /// Draws only <paramref name="keep"/> (every other renderer of the effect is disabled) until the
        /// returned scope is disposed. Simulation is unaffected: only drawing is switched off.
        /// </summary>
        public System.IDisposable Isolate(Renderer keep)
        {
            var disabled = new List<Renderer>();
            foreach (var r in _renderers)
            {
                if (r != null && r != keep && r.enabled)
                {
                    r.enabled = false;
                    disabled.Add(r);
                }
            }
            return new RestoreRenderers(disabled);
        }

        sealed class RestoreRenderers : System.IDisposable
        {
            readonly List<Renderer> _renderers;
            public RestoreRenderers(List<Renderer> renderers) => _renderers = renderers;
            public void Dispose()
            {
                foreach (var r in _renderers)
                    if (r != null) r.enabled = true;
            }
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

            for (int p = 0; p < _allParticleSystems.Length; p++)
            {
                var ps = _allParticleSystems[p];
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
                    float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                    float extent = largest * 0.5f;
                    _maxParticleSize[p] = Mathf.Max(_maxParticleSize[p], largest * MaxScale(ps.transform));
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
        float[] _maxParticleSize;

        static float MaxScale(Transform t)
        {
            var s = t.lossyScale;
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }

        /// <summary>
        /// Lifts every particle renderer's maxParticleSize (a share of the screen height, 0.5 by default)
        /// in the capture copy. Capture cameras sit a few meters away, far closer than a game camera, so
        /// large particles would be clamped there and their size animation hidden.
        /// </summary>
        public void LiftMaxParticleSize()
        {
            _originalMaxParticleSize = new float[_allParticleSystems.Length];
            for (int p = 0; p < _allParticleSystems.Length; p++)
            {
                var r = _allParticleSystems[p].GetComponent<ParticleSystemRenderer>();
                _originalMaxParticleSize[p] = r != null ? r.maxParticleSize : float.PositiveInfinity;
                if (r != null)
                    r.maxParticleSize = Mathf.Max(r.maxParticleSize, LiftedMaxParticleSize);
            }
        }

        float[] _originalMaxParticleSize;

        /// <summary>
        /// Systems whose largest particle seen by <see cref="TryGetVisibleBounds"/> would have been clamped
        /// by their own maxParticleSize with the camera at <paramref name="distance"/>:
        /// (index into <see cref="SystemLabels"/>, particle size in m, original limit).
        /// </summary>
        public List<(int system, float size, float limit)> ClampedAt(float distance, float tanHalfFov)
        {
            var clamped = new List<(int, float, float)>();
            if (_originalMaxParticleSize == null)
                return clamped;
            float screenHeight = 2f * Mathf.Max(0.01f, distance) * tanHalfFov;
            for (int p = 0; p < _allParticleSystems.Length; p++)
                if (_maxParticleSize[p] / screenHeight > _originalMaxParticleSize[p])
                    clamped.Add((p, _maxParticleSize[p], _originalMaxParticleSize[p]));
            return clamped;
        }

        /// <summary>maxParticleSize used in captures: larger than any particle a capture can frame.</summary>
        public const float LiftedMaxParticleSize = 10f;

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
