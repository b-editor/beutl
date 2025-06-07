namespace Beutl.Graphics.Rendering;

/// <summary>
/// ダミーメッシュ（空のメッシュ）
/// </summary>
internal class DummyMesh : I3DMesh
{
    public static readonly DummyMesh Instance = new();

    private DummyMesh() { }

    public ReadOnlySpan<System.Numerics.Vector3> Vertices => ReadOnlySpan<System.Numerics.Vector3>.Empty;
    public ReadOnlySpan<System.Numerics.Vector3> Normals => ReadOnlySpan<System.Numerics.Vector3>.Empty;
    public ReadOnlySpan<System.Numerics.Vector2> TexCoords => ReadOnlySpan<System.Numerics.Vector2>.Empty;
    public ReadOnlySpan<uint> Indices => ReadOnlySpan<uint>.Empty;
}
