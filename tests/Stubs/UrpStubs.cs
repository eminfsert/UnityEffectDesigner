// Minimal URP API surface used by the toolkit, for compile checking only.
namespace UnityEngine.Rendering.Universal
{
    public enum AntialiasingMode { None, FastApproximateAntialiasing, SubpixelMorphologicalAntiAliasing, TemporalAntiAliasing }
    public class UniversalAdditionalCameraData : UnityEngine.MonoBehaviour
    {
        public bool renderPostProcessing { get; set; }
        public AntialiasingMode antialiasing { get; set; }
        public UnityEngine.LayerMask volumeLayerMask { get; set; }
    }
    public static class CameraExtensions
    {
        public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this UnityEngine.Camera camera) => null;
    }
}
