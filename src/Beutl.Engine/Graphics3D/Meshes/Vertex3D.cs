using System.Numerics;
using System.Runtime.InteropServices;

namespace Beutl.Graphics3D.Meshes;

/// <summary>
/// Standard 3D vertex with position, normal, and texture coordinates.
/// This is a backend-agnostic representation.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vertex3D
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 TexCoord;

    public Vertex3D(Vector3 position, Vector3 normal, Vector2 texCoord)
    {
        Position = position;
        Normal = normal;
        TexCoord = texCoord;
    }
}
