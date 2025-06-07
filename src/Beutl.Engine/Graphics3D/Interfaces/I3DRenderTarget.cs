namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dレンダーターゲット
/// </summary>
public interface I3DRenderTarget : IDisposable
{
    int Width { get; }
    int Height { get; }
    TextureFormat ColorFormat { get; }
    TextureFormat? DepthFormat { get; }
}
