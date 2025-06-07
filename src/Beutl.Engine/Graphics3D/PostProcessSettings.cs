namespace Beutl.Graphics.Rendering;

/// <summary>
/// ポストプロセス設定
/// </summary>
public class PostProcessSettings
{
    public float Exposure { get; set; } = 1.0f;
    public float Gamma { get; set; } = 2.2f;
    public bool EnableToneMapping { get; set; } = true;
    public bool EnableGammaCorrection { get; set; } = true;
}
