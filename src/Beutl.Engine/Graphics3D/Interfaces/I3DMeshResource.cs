namespace Beutl.Graphics.Rendering;

/// <summary>
/// GPU上のメッシュリソース
/// </summary>
public interface I3DMeshResource : IDisposable
{
    uint VertexBufferId { get; }
    uint IndexBufferId { get; }
    int IndexCount { get; }
    I3DMesh SourceMesh { get; }
}
