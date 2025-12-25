using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;

namespace Beutl.Graphics3D.Meshes;

/// <summary>
/// A procedural cube mesh.
/// </summary>
public sealed partial class CubeMesh : Mesh
{
    public CubeMesh()
    {
        ScanProperties<CubeMesh>();
    }

    /// <summary>
    /// Gets the width of the cube (X-axis).
    /// </summary>
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the height of the cube (Y-axis).
    /// </summary>
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Height { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the depth of the cube (Z-axis).
    /// </summary>
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Depth { get; } = Property.CreateAnimatable(1f);

    /// <inheritdoc />
    public override void ApplyTo(Mesh.Resource resource, out Vertex3D[] vertices, out uint[] indices)
    {
        var r = (Resource)resource;
        GenerateCube(r.Width, r.Height, r.Depth, out vertices, out indices);
    }

    /// <summary>
    /// Generates cube mesh data with the specified dimensions.
    /// </summary>
    public static void GenerateCube(float width, float height, float depth, out Vertex3D[] vertices, out uint[] indices)
    {
        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        float halfD = depth * 0.5f;

        // 24 vertices (4 per face, 6 faces) with unique normals
        vertices =
        [
            // Front face (Z+)
            new(new Vector3(-halfW, -halfH, halfD), new Vector3(0, 0, 1), new Vector2(0, 1)),
            new(new Vector3(halfW, -halfH, halfD), new Vector3(0, 0, 1), new Vector2(1, 1)),
            new(new Vector3(halfW, halfH, halfD), new Vector3(0, 0, 1), new Vector2(1, 0)),
            new(new Vector3(-halfW, halfH, halfD), new Vector3(0, 0, 1), new Vector2(0, 0)),

            // Back face (Z-)
            new(new Vector3(halfW, -halfH, -halfD), new Vector3(0, 0, -1), new Vector2(0, 1)),
            new(new Vector3(-halfW, -halfH, -halfD), new Vector3(0, 0, -1), new Vector2(1, 1)),
            new(new Vector3(-halfW, halfH, -halfD), new Vector3(0, 0, -1), new Vector2(1, 0)),
            new(new Vector3(halfW, halfH, -halfD), new Vector3(0, 0, -1), new Vector2(0, 0)),

            // Top face (Y+)
            new(new Vector3(-halfW, halfH, halfD), new Vector3(0, 1, 0), new Vector2(0, 1)),
            new(new Vector3(halfW, halfH, halfD), new Vector3(0, 1, 0), new Vector2(1, 1)),
            new(new Vector3(halfW, halfH, -halfD), new Vector3(0, 1, 0), new Vector2(1, 0)),
            new(new Vector3(-halfW, halfH, -halfD), new Vector3(0, 1, 0), new Vector2(0, 0)),

            // Bottom face (Y-)
            new(new Vector3(-halfW, -halfH, -halfD), new Vector3(0, -1, 0), new Vector2(0, 1)),
            new(new Vector3(halfW, -halfH, -halfD), new Vector3(0, -1, 0), new Vector2(1, 1)),
            new(new Vector3(halfW, -halfH, halfD), new Vector3(0, -1, 0), new Vector2(1, 0)),
            new(new Vector3(-halfW, -halfH, halfD), new Vector3(0, -1, 0), new Vector2(0, 0)),

            // Right face (X+)
            new(new Vector3(halfW, -halfH, halfD), new Vector3(1, 0, 0), new Vector2(0, 1)),
            new(new Vector3(halfW, -halfH, -halfD), new Vector3(1, 0, 0), new Vector2(1, 1)),
            new(new Vector3(halfW, halfH, -halfD), new Vector3(1, 0, 0), new Vector2(1, 0)),
            new(new Vector3(halfW, halfH, halfD), new Vector3(1, 0, 0), new Vector2(0, 0)),

            // Left face (X-)
            new(new Vector3(-halfW, -halfH, -halfD), new Vector3(-1, 0, 0), new Vector2(0, 1)),
            new(new Vector3(-halfW, -halfH, halfD), new Vector3(-1, 0, 0), new Vector2(1, 1)),
            new(new Vector3(-halfW, halfH, halfD), new Vector3(-1, 0, 0), new Vector2(1, 0)),
            new(new Vector3(-halfW, halfH, -halfD), new Vector3(-1, 0, 0), new Vector2(0, 0)),
        ];

        // 36 indices (6 per face, 6 faces)
        indices =
        [
            // Front
            0, 1, 2, 0, 2, 3,
            // Back
            4, 5, 6, 4, 6, 7,
            // Top
            8, 9, 10, 8, 10, 11,
            // Bottom
            12, 13, 14, 12, 14, 15,
            // Right
            16, 17, 18, 16, 18, 19,
            // Left
            20, 21, 22, 20, 22, 23
        ];
    }
}
