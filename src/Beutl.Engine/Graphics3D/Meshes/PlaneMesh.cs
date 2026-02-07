using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics3D.Meshes;

/// <summary>
/// A procedural plane mesh on the XZ plane with Y+ normal.
/// </summary>
[Display(Name = nameof(Strings.PlaneMesh), ResourceType = typeof(Strings))]
public sealed partial class PlaneMesh : Mesh
{
    public PlaneMesh()
    {
        ScanProperties<PlaneMesh>();
    }

    /// <summary>
    /// Gets the width of the plane (X-axis).
    /// </summary>
    [Display(Name = nameof(Strings.Width), ResourceType = typeof(Strings))]
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the height of the plane (Z-axis).
    /// </summary>
    [Display(Name = nameof(Strings.Height), ResourceType = typeof(Strings))]
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Height { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the number of segments along the width (X-axis).
    /// </summary>
    [Display(Name = nameof(Strings.WidthSegments), ResourceType = typeof(Strings))]
    [Range(1, int.MaxValue)]
    public IProperty<int> WidthSegments { get; } = Property.CreateAnimatable(1);

    /// <summary>
    /// Gets the number of segments along the height (Z-axis).
    /// </summary>
    [Display(Name = nameof(Strings.HeightSegments), ResourceType = typeof(Strings))]
    [Range(1, int.MaxValue)]
    public IProperty<int> HeightSegments { get; } = Property.CreateAnimatable(1);

    /// <inheritdoc />
    public override void ApplyTo(Mesh.Resource resource, out Vertex3D[] vertices, out uint[] indices)
    {
        var r = (Resource)resource;
        GeneratePlane(r.Width, r.Height, r.WidthSegments, r.HeightSegments, out vertices, out indices);
    }

    /// <summary>
    /// Generates plane mesh data on the XZ plane with the specified dimensions and segments.
    /// </summary>
    public static void GeneratePlane(float width, float height, int widthSegments, int heightSegments, out Vertex3D[] vertices, out uint[] indices)
    {
        int xSegs = Math.Max(widthSegments, 1);
        int zSegs = Math.Max(heightSegments, 1);

        int vertCountX = xSegs + 1;
        int vertCountZ = zSegs + 1;

        float halfW = width * 0.5f;
        float halfH = height * 0.5f;

        var normal = new Vector3(0, 1, 0);
        var tangent = new Vector4(1, 0, 0, 1);

        vertices = new Vertex3D[vertCountX * vertCountZ];
        indices = new uint[xSegs * zSegs * 6];

        // Generate vertices
        int vi = 0;
        for (int iz = 0; iz < vertCountZ; iz++)
        {
            float tz = (float)iz / zSegs;
            float z = -halfH + tz * height;

            for (int ix = 0; ix < vertCountX; ix++)
            {
                float tx = (float)ix / xSegs;
                float x = -halfW + tx * width;

                vertices[vi++] = new Vertex3D(
                    new Vector3(x, 0, z),
                    normal,
                    new Vector2(tx, tz),
                    tangent);
            }
        }

        // Generate indices (CCW winding when viewed from Y+)
        int ii = 0;
        for (int iz = 0; iz < zSegs; iz++)
        {
            for (int ix = 0; ix < xSegs; ix++)
            {
                uint topLeft = (uint)(iz * vertCountX + ix);
                uint topRight = topLeft + 1;
                uint bottomLeft = (uint)((iz + 1) * vertCountX + ix);
                uint bottomRight = bottomLeft + 1;

                // First triangle (CCW)
                indices[ii++] = topLeft;
                indices[ii++] = topRight;
                indices[ii++] = bottomRight;

                // Second triangle (CCW)
                indices[ii++] = topLeft;
                indices[ii++] = bottomRight;
                indices[ii++] = bottomLeft;
            }
        }
    }
}
