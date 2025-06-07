namespace Beutl.Graphics.Rendering;

/// <summary>
/// レンダリングパイプライン設定
/// </summary>
public class RenderPipelineSettings
{
    public bool EnableDeferred { get; set; } = true;
    public bool EnableShadows { get; set; } = true;
    public bool EnableEnvironmentMapping { get; set; } = true;
    public int ShadowMapSize { get; set; } = 2048;
    public int MaxLights { get; set; } = 32;
    public float ShadowBias { get; set; } = 0.001f;
}
