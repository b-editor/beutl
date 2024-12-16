using System.Numerics;

namespace Beutl.Graphics3D.Meshes;

public readonly struct Vertex
{
    public Vertex(Vector3 pos) : this(pos, Vector3.One, Vector3.Zero, Vector2.Zero) { }

    public Vertex(Vector3 pos, Vector3 col) : this(pos, col, Vector3.Zero, Vector2.Zero) { }

    public Vertex(Vector3 pos, Vector3 col, Vector3 normal) : this(pos, col, normal, Vector2.Zero) { }

    public Vertex(Vector3 pos, Vector3 col, Vector3 normal, Vector2 texcoord)
    {
        Position = pos;
        Color = col;
        Normal = normal;
        TexCoord = texcoord;
        Tangent = normal;
        Bitangent = normal;
    }

    public Vector3 Position { get; }

    public Vector3 Color { get; }

    public Vector3 Normal { get; }

    public Vector2 TexCoord { get; }

    public Vector3 Tangent { get; }

    public Vector3 Bitangent { get; }

    public Vertex WithNormal(Vector3 normal)
    {
        return new(Position, Color, normal, TexCoord);
    }

    public Vertex WithTexCoord(Vector2 texcoord)
    {
        return new(Position, Color, Normal, texcoord);
    }

    public Vertex WithColor(Vector3 color)
    {
        return new(Position, color, Normal, TexCoord);
    }

    public Vertex WithPosition(Vector3 position)
    {
        return new(position, Color, Normal, TexCoord);
    }
}
