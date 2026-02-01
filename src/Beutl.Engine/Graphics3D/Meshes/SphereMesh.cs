using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics3D.Meshes;

/// <summary>
/// A procedural UV sphere mesh.
/// </summary>
[Display(Name = nameof(Strings.SphereMesh), ResourceType = typeof(Strings))]
public sealed partial class SphereMesh : Mesh
{
    public SphereMesh()
    {
        ScanProperties<SphereMesh>();
    }

    /// <summary>
    /// Gets the radius of the sphere.
    /// </summary>
    [Display(Name = nameof(Strings.Radius), ResourceType = typeof(Strings))]
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Radius { get; } = Property.CreateAnimatable(0.5f);

    /// <summary>
    /// Gets the number of horizontal segments (longitude).
    /// </summary>
    [Display(Name = nameof(Strings.Segments), ResourceType = typeof(Strings))]
    [Range(3, 128)]
    public IProperty<int> Segments { get; } = Property.CreateAnimatable(32);

    /// <summary>
    /// Gets the number of vertical rings (latitude).
    /// </summary>
    [Display(Name = nameof(Strings.Rings), ResourceType = typeof(Strings))]
    [Range(2, 128)]
    public IProperty<int> Rings { get; } = Property.CreateAnimatable(16);

    /// <inheritdoc />
    public override void ApplyTo(Mesh.Resource resource, out Vertex3D[] vertices, out uint[] indices)
    {
        var r = (Resource)resource;
        GenerateSphere(r.Radius, r.Segments, r.Rings, out vertices, out indices);
    }

    /// <summary>
    /// Generates UV sphere mesh data with the specified parameters.
    /// </summary>
    public static void GenerateSphere(float radius, int segments, int rings, out Vertex3D[] vertices, out uint[] indices)
    {
        var vertexList = new List<Vertex3D>();
        var indexList = new List<uint>();

        // Generate vertices
        for (int ring = 0; ring <= rings; ring++)
        {
            float v = (float)ring / rings;
            float phi = v * MathF.PI; // 0 to PI (top to bottom)

            for (int seg = 0; seg <= segments; seg++)
            {
                float u = (float)seg / segments;
                float theta = u * 2.0f * MathF.PI; // 0 to 2PI (around)

                // Spherical to Cartesian coordinates
                float sinPhi = MathF.Sin(phi);
                float cosPhi = MathF.Cos(phi);
                float sinTheta = MathF.Sin(theta);
                float cosTheta = MathF.Cos(theta);

                float x = sinPhi * cosTheta;
                float y = cosPhi;
                float z = sinPhi * sinTheta;

                var position = new Vector3(x * radius, y * radius, z * radius);
                var normal = new Vector3(x, y, z); // Normal is same as position direction for sphere
                var texCoord = new Vector2(u, v);

                // Tangent is in the direction of increasing theta (horizontal/U direction)
                // dPosition/dTheta = (-sinPhi * sinTheta, 0, sinPhi * cosTheta)
                // Normalized: (-sinTheta, 0, cosTheta)
                Vector3 tangentDir;
                if (MathF.Abs(sinPhi) > 0.0001f)
                {
                    tangentDir = Vector3.Normalize(new Vector3(-sinTheta, 0, cosTheta));
                }
                else
                {
                    // At poles, use arbitrary tangent perpendicular to normal
                    tangentDir = new Vector3(1, 0, 0);
                }
                var tangent = new Vector4(tangentDir, 1.0f);

                vertexList.Add(new Vertex3D(position, normal, texCoord, tangent));
            }
        }

        // Generate indices
        int vertsPerRing = segments + 1;

        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                uint current = (uint)(ring * vertsPerRing + seg);
                uint next = current + 1;
                uint below = (uint)((ring + 1) * vertsPerRing + seg);
                uint belowNext = below + 1;

                // Two triangles per quad
                // Triangle 1
                indexList.Add(current);
                indexList.Add(below);
                indexList.Add(next);

                // Triangle 2
                indexList.Add(next);
                indexList.Add(below);
                indexList.Add(belowNext);
            }
        }

        vertices = vertexList.ToArray();
        indices = indexList.ToArray();
    }
}
