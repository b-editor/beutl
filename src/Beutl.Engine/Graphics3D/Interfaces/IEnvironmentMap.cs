namespace Beutl.Graphics.Rendering;

/// <summary>
/// 環境マップインターフェース
/// </summary>
public interface IEnvironmentMap : IDisposable
{
    ITexture EnvironmentTexture { get; }
    ITexture? IrradianceTexture { get; }
    ITexture? PrefilterTexture { get; }
    ITexture? BrdfLutTexture { get; }
}
