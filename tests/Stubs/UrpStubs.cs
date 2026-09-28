// Minimal URP API surface used by the toolkit, for compile checking only.
namespace UnityEngine.Rendering.Universal
{
    public enum AntialiasingMode { None, FastApproximateAntialiasing, SubpixelMorphologicalAntiAliasing, TemporalAntiAliasing }
    public enum ColorGradingMode { LowDynamicRange, HighDynamicRange }
    public class UniversalRenderPipelineAsset : UnityEngine.Rendering.RenderPipelineAsset
    {
        public bool supportsHDR { get; set; }
        public ColorGradingMode colorGradingMode { get; set; }
        protected override UnityEngine.Rendering.RenderPipeline CreatePipeline() => null;
    }
    public class UniversalAdditionalCameraData : UnityEngine.MonoBehaviour
    {
        public bool renderPostProcessing { get; set; }
        public AntialiasingMode antialiasing { get; set; }
        public UnityEngine.LayerMask volumeLayerMask { get; set; }
        public UnityEngine.Rendering.VolumeStack volumeStack { get; set; }
    }
    public static class CameraExtensions
    {
        public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this UnityEngine.Camera camera) => null;
    }
}

// Volume framework surface used by the capture (Core RP + URP), for compile checks only.
namespace UnityEngine.Rendering
{
    public class VolumeParameter<T> { public T value; }
    public class VolumeComponent : UnityEngine.ScriptableObject { public bool IsActive() => false; }
    public class VolumeProfile : UnityEngine.ScriptableObject { }
    public class VolumeStack { public T GetComponent<T>() where T : VolumeComponent => null; }
    public sealed class VolumeManager
    {
        public static VolumeManager instance { get; } = new VolumeManager();
        public VolumeStack stack { get; set; }
    }
    public class Volume : UnityEngine.MonoBehaviour
    {
        public bool isGlobal;
        public float priority;
        public float weight;
        public VolumeProfile sharedProfile;
    }
}

namespace UnityEngine.Rendering.Universal
{
    public enum TonemappingMode { None, Neutral, ACES }
    public sealed class Tonemapping : UnityEngine.Rendering.VolumeComponent
    {
        public UnityEngine.Rendering.VolumeParameter<TonemappingMode> mode;
    }
    public sealed class Bloom : UnityEngine.Rendering.VolumeComponent
    {
        public UnityEngine.Rendering.VolumeParameter<float> threshold;
        public UnityEngine.Rendering.VolumeParameter<float> intensity;
    }
}
