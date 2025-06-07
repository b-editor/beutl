namespace Beutl.Graphics.Rendering;

/// <summary>
/// GPU上のマテリアルリソース
/// </summary>
public interface I3DMaterialResource : IDisposable
{
    I3DMaterial SourceMaterial { get; }
    ITextureResource? AlbedoTexture { get; }
    ITextureResource? NormalTexture { get; }
    ITextureResource? MetallicRoughnessTexture { get; }
}
