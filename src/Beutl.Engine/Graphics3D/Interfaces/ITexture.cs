namespace Beutl.Graphics.Rendering;

/// <summary>
/// テクスチャインターフェース
/// </summary>
public interface ITexture : IDisposable
{
    int Width { get; }
    int Height { get; }
    TextureFormat Format { get; }
}
