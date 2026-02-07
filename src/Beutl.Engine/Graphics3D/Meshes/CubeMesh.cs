using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics3D.Meshes;

/// <summary>
/// A procedural cube mesh.
/// </summary>
[Display(Name = nameof(Strings.CubeMesh), ResourceType = typeof(Strings))]
public sealed partial class CubeMesh : Mesh
{
    public CubeMesh()
    {
        ScanProperties<CubeMesh>();
    }

    /// <summary>
    /// Gets the width of the cube (X-axis).
    /// </summary>
    [Display(Name = nameof(Strings.Width), ResourceType = typeof(Strings))]
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the height of the cube (Y-axis).
    /// </summary>
    [Display(Name = nameof(Strings.Height), ResourceType = typeof(Strings))]
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Height { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the depth of the cube (Z-axis).
    /// </summary>
    [Display(Name = nameof(Strings.Depth), ResourceType = typeof(Strings))]
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

        // Tangent vectors for each face (aligned with UV U direction)
        var tangentFront = new Vector4(1, 0, 0, 1);   // Front (Z+): tangent along X+
        var tangentBack = new Vector4(-1, 0, 0, 1);  // Back (Z-): tangent along X-
        var tangentTop = new Vector4(1, 0, 0, 1);    // Top (Y+): tangent along X+
        var tangentBottom = new Vector4(1, 0, 0, 1); // Bottom (Y-): tangent along X+
        var tangentRight = new Vector4(0, 0, -1, 1); // Right (X+): tangent along Z-
        var tangentLeft = new Vector4(0, 0, 1, 1);   // Left (X-): tangent along Z+

        // 24 vertices (4 per face, 6 faces) with unique normals and tangents
        vertices =
        [
            // Front face (Z+)
            new(new Vector3(-halfW, -halfH, halfD), new Vector3(0, 0, 1), new Vector2(0, 1), tangentFront),
            new(new Vector3(halfW, -halfH, halfD), new Vector3(0, 0, 1), new Vector2(1, 1), tangentFront),
            new(new Vector3(halfW, halfH, halfD), new Vector3(0, 0, 1), new Vector2(1, 0), tangentFront),
            new(new Vector3(-halfW, halfH, halfD), new Vector3(0, 0, 1), new Vector2(0, 0), tangentFront),

            // Back face (Z-)
            new(new Vector3(halfW, -halfH, -halfD), new Vector3(0, 0, -1), new Vector2(0, 1), tangentBack),
            new(new Vector3(-halfW, -halfH, -halfD), new Vector3(0, 0, -1), new Vector2(1, 1), tangentBack),
            new(new Vector3(-halfW, halfH, -halfD), new Vector3(0, 0, -1), new Vector2(1, 0), tangentBack),
            new(new Vector3(halfW, halfH, -halfD), new Vector3(0, 0, -1), new Vector2(0, 0), tangentBack),

            // Top face (Y+)
            new(new Vector3(-halfW, halfH, halfD), new Vector3(0, 1, 0), new Vector2(0, 1), tangentTop),
            new(new Vector3(halfW, halfH, halfD), new Vector3(0, 1, 0), new Vector2(1, 1), tangentTop),
            new(new Vector3(halfW, halfH, -halfD), new Vector3(0, 1, 0), new Vector2(1, 0), tangentTop),
            new(new Vector3(-halfW, halfH, -halfD), new Vector3(0, 1, 0), new Vector2(0, 0), tangentTop),

            // Bottom face (Y-)
            new(new Vector3(-halfW, -halfH, -halfD), new Vector3(0, -1, 0), new Vector2(0, 1), tangentBottom),
            new(new Vector3(halfW, -halfH, -halfD), new Vector3(0, -1, 0), new Vector2(1, 1), tangentBottom),
            new(new Vector3(halfW, -halfH, halfD), new Vector3(0, -1, 0), new Vector2(1, 0), tangentBottom),
            new(new Vector3(-halfW, -halfH, halfD), new Vector3(0, -1, 0), new Vector2(0, 0), tangentBottom),

            // Right face (X+)
            new(new Vector3(halfW, -halfH, halfD), new Vector3(1, 0, 0), new Vector2(0, 1), tangentRight),
            new(new Vector3(halfW, -halfH, -halfD), new Vector3(1, 0, 0), new Vector2(1, 1), tangentRight),
            new(new Vector3(halfW, halfH, -halfD), new Vector3(1, 0, 0), new Vector2(1, 0), tangentRight),
            new(new Vector3(halfW, halfH, halfD), new Vector3(1, 0, 0), new Vector2(0, 0), tangentRight),

            // Left face (X-)
            new(new Vector3(-halfW, -halfH, -halfD), new Vector3(-1, 0, 0), new Vector2(0, 1), tangentLeft),
            new(new Vector3(-halfW, -halfH, halfD), new Vector3(-1, 0, 0), new Vector2(1, 1), tangentLeft),
            new(new Vector3(-halfW, halfH, halfD), new Vector3(-1, 0, 0), new Vector2(1, 0), tangentLeft),
            new(new Vector3(-halfW, halfH, -halfD), new Vector3(-1, 0, 0), new Vector2(0, 0), tangentLeft),
        ];

        // 36 indices (6 per face, 6 faces) - counter-clockwise winding for front faces
        indices =
        [
            // Front (looking at Z+, CCW = 0,2,1 then 0,3,2)
            0, 2, 1, 0, 3, 2,
            // Back (looking at Z-, CCW)
            4, 6, 5, 4, 7, 6,
            // Top (looking at Y+, CCW)
            8, 10, 9, 8, 11, 10,
            // Bottom (looking at Y-, CCW)
            12, 14, 13, 12, 15, 14,
            // Right (looking at X+, CCW)
            16, 18, 17, 16, 19, 18,
            // Left (looking at X-, CCW)
            20, 22, 21, 20, 23, 22
        ];
    }
}
