namespace Beutl.Graphics.Rendering;

/// <summary>
/// ダミーメッシュリソース（グループなどメッシュを持たないオブジェクト用）
/// </summary>
internal class DummyMeshResource : I3DMeshResource
{
    public static readonly DummyMeshResource Instance = new();

    private DummyMeshResource() { }

    public uint VertexBufferId => 0;
    public uint IndexBufferId => 0;
    public int IndexCount => 0;
    public I3DMesh SourceMesh => DummyMesh.Instance;

    public void Dispose()
    {
        // 何もしない
    }
}
