using System.Numerics;
using Beutl.Media;

namespace Beutl.Graphics3D;

public readonly struct Vertex : IVertexType
{
    public Vector3 Position { get; init; }

    public Vector2 TexCoord { get; init; }

    public Color Color { get; init; }

    public static VertexElementFormat[] Formats { get; } =
    [
        VertexElementFormat.Float3,
        VertexElementFormat.Float2,
        VertexElementFormat.UByte4Norm
    ];

    public static uint[] Offsets { get; } =
    [
        0,
        12,
        20
    ];
}
