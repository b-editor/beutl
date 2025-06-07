namespace Beutl.Graphics.Rendering;

/// <summary>
/// GPU上のテクスチャリソース
/// </summary>
public interface ITextureResource : IDisposable
{
    uint TextureId { get; }
    int Width { get; }
    int Height { get; }
    TextureFormat Format { get; }
}
