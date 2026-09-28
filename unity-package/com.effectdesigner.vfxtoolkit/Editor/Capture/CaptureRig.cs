using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if VFXTOOLKIT_URP
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace EffectDesigner.VFXToolkit.Editor.Capture
{
    /// <summary>Tonemapping and bloom a capture was rendered with.</summary>
    public sealed class PostProcessInfo
    {
        public string volumeProfile;
        /// <summary>Where the profile came from: the parameter, project settings, or "none (explicit)"; null if none was set anywhere.</summary>
        public string volumeProfileSource;
        public string tonemapping;
        public bool bloom;
        public float bloomThreshold;
        public float bloomIntensity;
        /// <summary>Quality level whose pipeline asset rendered the capture (each level can use another asset).</summary>
        public string qualityLevel;
        public string pipelineAsset;
        /// <summary>The asset's color grading mode: LDR grading clamps before grading; the game may differ per quality level.</summary>
        public string colorGrading;
        public bool pipelineHdr;
        /// <summary>The capture itself renders HDR (from toolkit 0.5.4), whatever the asset says.</summary>
        public bool captureHdr = true;
    }

    /// <summary>
    /// An isolated preview scene holding a copy of the effect, a capture camera and an
    /// optional key light. Nothing from the open scenes is rendered and nothing in them
    /// is modified. Volumes in the open scenes do not reach it either (SRP only blends volumes
    /// the camera's stage renders), so without a profile only the pipeline's default profiles
    /// (global + quality level + custom) apply, which may differ from the game scene: pass the
    /// game's VolumeProfile to render under its tonemapping, bloom and color adjustments.
    /// </summary>
    sealed class CaptureRig : IDisposable
    {
        readonly Scene _scene;
        readonly Camera _camera;
        readonly RenderTexture _target;
        readonly Texture2D _hdrReadback;
        readonly Texture2D _readback;
        Color32[] _pixels;
        readonly string _volumeProfilePath;

        public GameObject Effect { get; }
        public int FrameSize { get; }

        /// <summary>Layer of the optional ground plane: drawn in background-only renders too, so it counts as background.</summary>
        const int GroundLayer = 31;
        GameObject _ground;

        public CaptureRig(GameObject source, int frameSize, float fieldOfView, bool addLight, bool postProcessing, string volumeProfile = null)
        {
            _volumeProfilePath = string.IsNullOrWhiteSpace(volumeProfile) ? null : volumeProfile;
            FrameSize = frameSize;
            _scene = EditorSceneManager.NewPreviewScene();

            Effect = UnityEngine.Object.Instantiate(source);
            Effect.name = source.name;
            Effect.hideFlags = HideFlags.DontSave;
            SceneManager.MoveGameObjectToScene(Effect, _scene);
            Effect.transform.position = Vector3.zero;
            Effect.SetActive(true);

            if (addLight)
            {
                var lightGo = new GameObject("VFXCapture_KeyLight") { hideFlags = HideFlags.DontSave };
                SceneManager.MoveGameObjectToScene(lightGo, _scene);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                light.color = new Color(1f, 0.97f, 0.92f);
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            var cameraGo = new GameObject("VFXCapture_Camera") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(cameraGo, _scene);
            _camera = cameraGo.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.scene = _scene;
            _camera.cameraType = CameraType.Game;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.fieldOfView = fieldOfView;
            _camera.allowHDR = true;
            _camera.allowMSAA = true;
            ConfigurePipelineCamera(postProcessing);
            AddVolume();

            // HDR target: URP gives a camera with a targetTexture an intermediate color buffer in the
            // target's format, so an 8-bit target clipped every channel at 1 before bloom and
            // tonemapping (the game camera renders HDR). Half-float keeps the scene HDR; the frame is
            // converted to 8-bit sRGB on readback, after post-processing, like a display would.
            _target = new RenderTexture(frameSize, frameSize, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Linear)
            {
                antiAliasing = 4,
                hideFlags = HideFlags.DontSave,
            };
            _target.Create();
            _camera.targetTexture = _target;

            _hdrReadback = new Texture2D(frameSize, frameSize, TextureFormat.RGBAFloat, false, true)
            {
                hideFlags = HideFlags.DontSave,
            };
            _readback = new Texture2D(frameSize, frameSize, TextureFormat.RGBA32, false, false)
            {
                hideFlags = HideFlags.DontSave,
            };
        }

        void ConfigurePipelineCamera(bool postProcessing)
        {
#if VFXTOOLKIT_URP
            var data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = postProcessing;
            data.antialiasing = AntialiasingMode.None; // MSAA on the target is enough
            // Reuse the main camera's volume layers so project bloom/tonemapping apply as in game.
            var main = Camera.main;
            if (main != null && main.TryGetComponent<UniversalAdditionalCameraData>(out var mainData))
                data.volumeLayerMask = mainData.volumeLayerMask;
#endif
        }

        void AddVolume()
        {
            if (_volumeProfilePath == null)
                return;
#if VFXTOOLKIT_URP
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(_volumeProfilePath);
            if (profile == null)
            {
                // Thrown from the constructor, so Dispose will not run: close the scene here.
                EditorSceneManager.ClosePreviewScene(_scene);
                throw new ArgumentException($"No VolumeProfile at '{_volumeProfilePath}'.");
            }
            var go = new GameObject("VFXCapture_Volume") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(go, _scene);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10000f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
            var data = _camera.GetUniversalAdditionalCameraData();
            data.volumeLayerMask |= 1 << go.layer;
#else
            EditorSceneManager.ClosePreviewScene(_scene);
            throw new ArgumentException("volume_profile needs URP.");
#endif
        }

        /// <summary>
        /// Linear frame (after post-processing) to 8-bit sRGB, as a display shows it: values above 1
        /// (no tonemapping) clip there, after bloom and color grading had the full HDR range.
        /// </summary>
        internal static void ToDisplay(Color[] linear, Color32[] output, bool linearColorSpace)
        {
            for (int i = 0; i < linear.Length; i++)
            {
                var c = linear[i];
                output[i] = new Color32(Encode(c.r, linearColorSpace), Encode(c.g, linearColorSpace), Encode(c.b, linearColorSpace), (byte)Mathf.Clamp(Mathf.RoundToInt(c.a * 255f), 0, 255));
            }
        }

        static byte Encode(float value, bool linearColorSpace)
        {
            if (float.IsNaN(value)) value = 0f;
            float v = Mathf.Clamp01(value);
            if (!linearColorSpace)
                return (byte)Mathf.RoundToInt(v * 255f);
            return SrgbLut[(int)(v * (LutSize - 1) + 0.5f)];
        }

        // Linear -> 8-bit sRGB by table: pow() per channel per pixel made captures ~3x slower.
        // 16k steps keep every dark sRGB code reachable (the curve is steepest near 0).
        const int LutSize = 16384;
        static readonly byte[] SrgbLut = BuildSrgbLut();

        static byte[] BuildSrgbLut()
        {
            var lut = new byte[LutSize];
            for (int i = 0; i < LutSize; i++)
                lut[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(Recipes.ColorSpaceMath.LinearToSrgb(i / (float)(LutSize - 1)) * 255f), 0, 255);
            return lut;
        }

        /// <summary>Tonemapping and bloom the capture camera rendered with (call after a Render).</summary>
        public PostProcessInfo DescribePostProcessing()
        {
            var info = new PostProcessInfo { volumeProfile = _volumeProfilePath ?? "(pipeline defaults only: global + quality)", tonemapping = "unknown" };
            int level = QualitySettings.GetQualityLevel();
            info.qualityLevel = level >= 0 && level < QualitySettings.names.Length ? QualitySettings.names[level] : level.ToString();
#if VFXTOOLKIT_URP
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)
            {
                info.pipelineAsset = AssetDatabase.GetAssetPath(asset);
                info.colorGrading = asset.colorGradingMode.ToString();
                info.pipelineHdr = asset.supportsHDR;
            }
            // A camera with its own stack (volume updates "via scripting") does not use the shared one.
            var stack = _camera.GetUniversalAdditionalCameraData().volumeStack ?? VolumeManager.instance.stack;
            var tonemapping = stack?.GetComponent<Tonemapping>();
            if (tonemapping != null)
                info.tonemapping = tonemapping.IsActive() ? tonemapping.mode.value.ToString() : "None";
            var bloom = stack?.GetComponent<Bloom>();
            if (bloom != null)
            {
                info.bloom = bloom.IsActive();
                info.bloomThreshold = bloom.threshold.value;
                info.bloomIntensity = bloom.intensity.value;
            }
#endif
            return info;
        }

        public Transform CameraTransform => _camera.transform;
        public float TanHalfFov => Mathf.Tan(Mathf.Max(1f, _camera.fieldOfView * 0.5f) * Mathf.Deg2Rad);

        /// <summary>Camera distance at which a sphere of <paramref name="radius"/> fits the frame.</summary>
        public float DistanceToFit(float radius)
        {
            float halfFov = Mathf.Max(1f, _camera.fieldOfView * 0.5f) * Mathf.Deg2Rad;
            return radius / Mathf.Sin(halfFov) * 1.05f;
        }

        /// <summary>
        /// Points the camera at <paramref name="center"/> from the given direction.
        /// <paramref name="depthRadius"/> is the effect's size, used only for clip planes.
        /// </summary>
        public void Aim(Vector3 center, float distance, float depthRadius, CaptureView view)
        {
            Vector3 offset = Quaternion.Euler(view.Elevation, view.Azimuth, 0f) * (Vector3.back * distance);
            _camera.transform.position = center + offset;
            Vector3 up = Mathf.Abs(view.Elevation) > 80f ? Vector3.forward : Vector3.up;
            _camera.transform.LookAt(center, up);

            _camera.nearClipPlane = Mathf.Max(0.01f, distance - depthRadius * 2f);
            _camera.farClipPlane = distance + depthRadius * 4f;
        }

        /// <summary>Renders only the background and post-processing, with nothing in front of the camera.</summary>
        /// <summary>
        /// Adds an unlit ground plane of <paramref name="color"/> at height <paramref name="y"/> under the effect,
        /// so effects that sit on the ground read as in game and readability is measured against the ground.
        /// </summary>
        public void AddGround(Color color, float y, float size)
        {
            _ground = new GameObject("VFXCapture_Ground") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(_ground, _scene);
            _ground.layer = GroundLayer;
            _ground.transform.position = new Vector3(0f, y, 0f);
            float h = size * 0.5f;
            var quad = new Mesh { name = "VFXCapture_GroundQuad", hideFlags = HideFlags.DontSave };
            quad.SetVertices(new[] { new Vector3(-h, 0f, -h), new Vector3(-h, 0f, h), new Vector3(h, 0f, h), new Vector3(h, 0f, -h) });
            quad.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            quad.RecalculateBounds();
            _ground.AddComponent<MeshFilter>().sharedMesh = quad;
            _ground.AddComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            var renderer = _ground.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public Texture2D RenderBackgroundOnly(Color background)
        {
            int mask = _camera.cullingMask;
            _camera.cullingMask = _ground != null ? 1 << GroundLayer : 0;
            try
            {
                return Render(background);
            }
            finally
            {
                _camera.cullingMask = mask;
            }
        }

        /// <summary>Renders the current state and returns the frame (owned by the rig; copy before the next call).</summary>
        public Texture2D Render(Color background)
        {
            _camera.backgroundColor = background;
            _camera.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = _target;
            _hdrReadback.ReadPixels(new Rect(0, 0, FrameSize, FrameSize), 0, 0, false);
            _hdrReadback.Apply(false);
            _pixels ??= new Color32[FrameSize * FrameSize];
            ToDisplay(_hdrReadback.GetPixels(), _pixels, QualitySettings.activeColorSpace == ColorSpace.Linear);
            _readback.SetPixels32(_pixels);
            _readback.Apply(false);
            RenderTexture.active = previous;
            return _readback;
        }

        public void Dispose()
        {
            if (_camera != null)
                _camera.targetTexture = null;
            if (_target != null)
            {
                _target.Release();
                UnityEngine.Object.DestroyImmediate(_target);
            }
            if (_readback != null)
                UnityEngine.Object.DestroyImmediate(_readback);
            if (_hdrReadback != null)
                UnityEngine.Object.DestroyImmediate(_hdrReadback);
            if (_ground != null)
            {
                UnityEngine.Object.DestroyImmediate(_ground.GetComponent<MeshRenderer>().sharedMaterial);
                UnityEngine.Object.DestroyImmediate(_ground.GetComponent<MeshFilter>().sharedMesh);
            }
            // Closing the preview scene destroys the effect copy, camera and light.
            EditorSceneManager.ClosePreviewScene(_scene);
        }
    }
}
