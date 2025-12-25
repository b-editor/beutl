using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;

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

    /// <summary>
    /// Gets the vertex input description for <see cref="Vertex3D"/>.
    /// </summary>
    public static VertexInputDescription GetVertexInputDescription()
    {
        return new VertexInputDescription
        {
            Bindings =
            [
                new VertexBindingDescription
                {
                    Binding = 0,
                    Stride = (uint)Marshal.SizeOf<Vertex3D>(),
                    InputRate = VertexInputRate.Vertex
                }
            ],
            Attributes =
            [
                new VertexAttributeDescription
                {
                    Binding = 0,
                    Location = 0,
                    Format = VertexFormat.Float3,
                    Offset = 0
                },
                new VertexAttributeDescription
                {
                    Binding = 0,
                    Location = 1,
                    Format = VertexFormat.Float3,
                    Offset = (uint)Marshal.OffsetOf<Vertex3D>(nameof(Normal))
                },
                new VertexAttributeDescription
                {
                    Binding = 0,
                    Location = 2,
                    Format = VertexFormat.Float2,
                    Offset = (uint)Marshal.OffsetOf<Vertex3D>(nameof(TexCoord))
                }
            ]
        };
    }
}
