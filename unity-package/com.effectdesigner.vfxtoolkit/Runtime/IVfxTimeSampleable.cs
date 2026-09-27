namespace EffectDesigner.VFXToolkit
{
    /// <summary>
    /// Implemented by effect components whose state depends on elapsed time but is not
    /// driven by a ParticleSystem or VisualEffect (mesh scale curves, light flashes,
    /// material property animation, camera shake previews...).
    ///
    /// The timeline capture calls <see cref="SampleAt"/> in edit mode before rendering
    /// each frame, so the component must set its full visual state for that time
    /// deterministically, without relying on Update() or Time.time.
    /// </summary>
    public interface IVfxTimeSampleable
    {
        /// <param name="time">Seconds since the effect started playing.</param>
        void SampleAt(float time);
    }
}
