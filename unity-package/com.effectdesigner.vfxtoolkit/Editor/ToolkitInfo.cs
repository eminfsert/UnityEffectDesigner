namespace EffectDesigner.VFXToolkit.Editor
{
    /// <summary>
    /// Version reported in every tool result, so agents can tell which toolkit build they are
    /// talking to (the plugin's instructions name the minimum they need). Kept equal to
    /// package.json by tests/run.sh.
    /// </summary>
    public static class ToolkitInfo
    {
        public const string Version = "0.4.2";
    }
}
