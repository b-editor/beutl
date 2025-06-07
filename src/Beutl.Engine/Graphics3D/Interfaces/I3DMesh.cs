using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dメッシュデータインターフェース
/// </summary>
public interface I3DMesh
{
    ReadOnlySpan<Vector3> Vertices { get; }
    ReadOnlySpan<Vector3> Normals { get; }
    ReadOnlySpan<Vector2> TexCoords { get; }
    ReadOnlySpan<uint> Indices { get; }
}
