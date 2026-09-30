using System.Numerics;
using Beutl.Graphics3D.Meshes;

namespace Beutl.Graphics3D;

/// <summary>
/// Beutl's 3D space shares the 2D canvas axes: +X points right, +Y points down and +Z points away from the
/// viewer, with one unit per pixel.
/// </summary>
/// <remarks>
/// Procedural meshes are authored, and most model files are stored, with +Y up. A half turn about X carries
/// that frame into Beutl's: it is a rotation, so handedness and triangle winding are unchanged and a mesh
/// seen from the default camera looks exactly as its Y-up authoring intended.
/// </remarks>
internal static class CoordinateSystem3D
{
    public static Vector3 FromYUp(Vector3 value) => new(value.X, -value.Y, -value.Z);

    public static Vertex3D FromYUp(Vertex3D vertex)
    {
        Vector3 tangent = FromYUp(new Vector3(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z));
        return new Vertex3D(
            FromYUp(vertex.Position),
            FromYUp(vertex.Normal),
            vertex.TexCoord,
            new Vector4(tangent, vertex.Tangent.W));
    }

    public static void ConvertFromYUp(Span<Vertex3D> vertices)
    {
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = FromYUp(vertices[i]);
        }
    }
}
