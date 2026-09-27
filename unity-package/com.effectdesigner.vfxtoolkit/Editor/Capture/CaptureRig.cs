using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if VFXTOOLKIT_URP
using UnityEngine.Rendering.Universal;
#endif

namespace EffectDesigner.VFXToolkit.Editor.Capture
{
    /// <summary>
    /// An isolated preview scene holding a copy of the effect, a capture camera and an
    /// optional key light. Nothing from the open scenes is rendered and nothing in them
    /// is modified; project-wide global Volumes (bloom, tonemapping) still apply.
    /// </summary>
    sealed class CaptureRig : IDisposable
    {
        readonly Scene _scene;
        readonly Camera _camera;
        readonly RenderTexture _target;
        readonly Texture2D _readback;

        public GameObject Effect { get; }
        public int FrameSize { get; }

        public CaptureRig(GameObject source, int frameSize, float fieldOfView, bool addLight, bool postProcessing)
        {
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

            _target = new RenderTexture(frameSize, frameSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 4,
                hideFlags = HideFlags.DontSave,
            };
            _target.Create();
            _camera.targetTexture = _target;

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
        public Texture2D RenderBackgroundOnly(Color background)
        {
            int mask = _camera.cullingMask;
            _camera.cullingMask = 0;
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
            _readback.ReadPixels(new Rect(0, 0, FrameSize, FrameSize), 0, 0, false);
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
            // Closing the preview scene destroys the effect copy, camera and light.
            EditorSceneManager.ClosePreviewScene(_scene);
        }
    }
}
