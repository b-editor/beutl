using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;

namespace Beutl.Graphics3D.Gizmo;

/// <summary>
/// Vertex structure for gizmo rendering with position and color.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GizmoVertex
{
    public Vector3 Position;
    public Vector3 Color;

    public GizmoVertex(Vector3 position, Vector3 color)
    {
        Position = position;
        Color = color;
    }

    /// <summary>
    /// Gets the vertex input description for <see cref="GizmoVertex"/>.
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
                    Stride = (uint)Marshal.SizeOf<GizmoVertex>(),
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
                    Offset = (uint)Marshal.OffsetOf<GizmoVertex>(nameof(Color))
                }
            ]
        };
    }
}

/// <summary>
/// Static class for generating gizmo mesh geometry.
/// </summary>
internal static class GizmoMesh
{
    // Axis colors
    public static readonly Vector3 XAxisColor = new(1f, 0.2f, 0.2f); // Red
    public static readonly Vector3 YAxisColor = new(0.2f, 1f, 0.2f); // Green
    public static readonly Vector3 ZAxisColor = new(0.2f, 0.4f, 1f); // Blue

    // Plane colors (mixed colors with alpha indicated by reduced intensity)
    public static readonly Vector3 XYPlaneColor = ZAxisColor;
    public static readonly Vector3 YZPlaneColor = XAxisColor;
    public static readonly Vector3 ZXPlaneColor = YAxisColor;
    public static readonly Vector3 CenterColor = new(0.9f, 0.9f, 0.9f); // White/Gray for center

    // Gizmo dimensions
    private const float ArrowLength = 1.0f;
    private const float ArrowShaftRadius = 0.02f;
    private const float ArrowHeadRadius = 0.06f;
    private const float ArrowHeadLength = 0.15f;
    private const int CircleSegments = 16;

    private const float RotateRingRadius = 0.8f;
    private const float RotateRingThickness = 0.02f;
    private const int RotateRingSegments = 48;

    private const float ScaleLineLength = 0.8f;
    private const float ScaleCubeSize = 0.08f;

    // Plane dimensions for translate mode
    private const float PlaneOffset = 0.0f; // Distance from center
    private const float PlaneSize = 0.2f; // Size of plane indicator

    // Center cube for uniform scale
    private const float CenterCubeSize = 0.12f;

    /// <summary>
    /// Creates translation gizmo geometry (3 arrows along X, Y, Z axes plus plane indicators).
    /// </summary>
    public static void CreateTranslateGizmo(out GizmoVertex[] vertices, out uint[] indices)
    {
        var vertexList = new List<GizmoVertex>();
        var indexList = new List<uint>();

        // X axis arrow (red)
        CreateArrow(Vector3.Zero, Vector3.UnitX, XAxisColor, vertexList, indexList);

        // Y axis arrow (green)
        CreateArrow(Vector3.Zero, Vector3.UnitY, YAxisColor, vertexList, indexList);

        // Z axis arrow (blue)
        CreateArrow(Vector3.Zero, Vector3.UnitZ, ZAxisColor, vertexList, indexList);

        // XY plane indicator (yellow)
        CreatePlaneQuad(Vector3.UnitX, Vector3.UnitY, XYPlaneColor, vertexList, indexList);

        // YZ plane indicator (cyan)
        CreatePlaneQuad(Vector3.UnitY, Vector3.UnitZ, YZPlaneColor, vertexList, indexList);

        // ZX plane indicator (magenta)
        CreatePlaneQuad(Vector3.UnitZ, Vector3.UnitX, ZXPlaneColor, vertexList, indexList);

        vertices = vertexList.ToArray();
        indices = indexList.ToArray();
    }

    /// <summary>
    /// Creates rotation gizmo geometry (3 rings for X, Y, Z rotation).
    /// </summary>
    public static void CreateRotateGizmo(out GizmoVertex[] vertices, out uint[] indices)
    {
        var vertexList = new List<GizmoVertex>();
        var indexList = new List<uint>();

        // X axis ring (red) - rotates around X, so ring is in YZ plane
        CreateRing(Vector3.UnitX, XAxisColor, vertexList, indexList);

        // Y axis ring (green) - rotates around Y, so ring is in XZ plane
        CreateRing(Vector3.UnitY, YAxisColor, vertexList, indexList);

        // Z axis ring (blue) - rotates around Z, so ring is in XY plane
        CreateRing(Vector3.UnitZ, ZAxisColor, vertexList, indexList);

        vertices = vertexList.ToArray();
        indices = indexList.ToArray();
    }

    /// <summary>
    /// Creates scale gizmo geometry (3 lines with cubes at the end plus center cube for uniform scale).
    /// </summary>
    public static void CreateScaleGizmo(out GizmoVertex[] vertices, out uint[] indices)
    {
        var vertexList = new List<GizmoVertex>();
        var indexList = new List<uint>();

        // X axis (red)
        CreateScaleAxis(Vector3.UnitX, XAxisColor, vertexList, indexList);

        // Y axis (green)
        CreateScaleAxis(Vector3.UnitY, YAxisColor, vertexList, indexList);

        // Z axis (blue)
        CreateScaleAxis(Vector3.UnitZ, ZAxisColor, vertexList, indexList);

        // Center cube for uniform scale (white/gray)
        CreateCube(Vector3.Zero, CenterCubeSize, CenterColor, vertexList, indexList);

        vertices = vertexList.ToArray();
        indices = indexList.ToArray();
    }

    private static void CreateArrow(Vector3 origin, Vector3 direction, Vector3 color, List<GizmoVertex> vertices, List<uint> indices)
    {
        uint baseIndex = (uint)vertices.Count;

        // Calculate perpendicular vectors for cylinder/cone
        var up = Math.Abs(Vector3.Dot(direction, Vector3.UnitY)) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        var right = Vector3.Normalize(Vector3.Cross(direction, up));
        up = Vector3.Normalize(Vector3.Cross(right, direction));

        float shaftLength = ArrowLength - ArrowHeadLength;

        // Create shaft (cylinder)
        for (int i = 0; i < CircleSegments; i++)
        {
            float angle = i * MathF.PI * 2 / CircleSegments;
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);

            var offset = right * cos * ArrowShaftRadius + up * sin * ArrowShaftRadius;

            // Bottom vertex
            vertices.Add(new GizmoVertex(origin + offset, color));
            // Top vertex (at shaft end)
            vertices.Add(new GizmoVertex(origin + direction * shaftLength + offset, color));
        }

        // Shaft indices (cylinder sides)
        for (int i = 0; i < CircleSegments; i++)
        {
            uint i0 = baseIndex + (uint)(i * 2);
            uint i1 = baseIndex + (uint)(i * 2 + 1);
            uint i2 = baseIndex + (uint)(((i + 1) % CircleSegments) * 2);
            uint i3 = baseIndex + (uint)(((i + 1) % CircleSegments) * 2 + 1);

            indices.Add(i0);
            indices.Add(i2);
            indices.Add(i1);
            indices.Add(i1);
            indices.Add(i2);
            indices.Add(i3);
        }

        // Create cone (arrow head)
        uint coneBaseIndex = (uint)vertices.Count;
        var coneBase = origin + direction * shaftLength;
        var coneTip = origin + direction * ArrowLength;

        // Cone base vertices
        for (int i = 0; i < CircleSegments; i++)
        {
            float angle = i * MathF.PI * 2 / CircleSegments;
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);

            var offset = right * cos * ArrowHeadRadius + up * sin * ArrowHeadRadius;
            vertices.Add(new GizmoVertex(coneBase + offset, color));
        }

        // Cone tip
        uint tipIndex = (uint)vertices.Count;
        vertices.Add(new GizmoVertex(coneTip, color));

        // Cone side indices
        for (int i = 0; i < CircleSegments; i++)
        {
            uint i0 = coneBaseIndex + (uint)i;
            uint i1 = coneBaseIndex + (uint)((i + 1) % CircleSegments);

            indices.Add(i0);
            indices.Add(i1);
            indices.Add(tipIndex);
        }

        // Cone base cap
        uint capCenterIndex = (uint)vertices.Count;
        vertices.Add(new GizmoVertex(coneBase, color));

        for (int i = 0; i < CircleSegments; i++)
        {
            uint i0 = coneBaseIndex + (uint)i;
            uint i1 = coneBaseIndex + (uint)((i + 1) % CircleSegments);

            indices.Add(i1);
            indices.Add(i0);
            indices.Add(capCenterIndex);
        }
    }

    private static void CreateRing(Vector3 axis, Vector3 color, List<GizmoVertex> vertices, List<uint> indices)
    {
        uint baseIndex = (uint)vertices.Count;

        // Calculate perpendicular vectors for the ring plane
        var up = Math.Abs(Vector3.Dot(axis, Vector3.UnitY)) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        var tangent1 = Vector3.Normalize(Vector3.Cross(axis, up));
        var tangent2 = Vector3.Normalize(Vector3.Cross(axis, tangent1));

        // Create torus (ring) - only draw 270 degrees for visibility
        float arcAngle = MathF.PI * 1.5f; // 270 degrees

        for (int i = 0; i <= RotateRingSegments; i++)
        {
            float majorAngle = i * arcAngle / RotateRingSegments;
            float majorCos = MathF.Cos(majorAngle);
            float majorSin = MathF.Sin(majorAngle);

            var ringCenter = tangent1 * majorCos * RotateRingRadius + tangent2 * majorSin * RotateRingRadius;
            var ringDir = Vector3.Normalize(ringCenter);

            // Create small circle around the ring center (tube cross-section)
            for (int j = 0; j < CircleSegments; j++)
            {
                float minorAngle = j * MathF.PI * 2 / CircleSegments;
                float minorCos = MathF.Cos(minorAngle);
                float minorSin = MathF.Sin(minorAngle);

                var tubeOffset = ringDir * minorCos * RotateRingThickness + axis * minorSin * RotateRingThickness;
                vertices.Add(new GizmoVertex(ringCenter + tubeOffset, color));
            }
        }

        // Indices for the torus
        for (int i = 0; i < RotateRingSegments; i++)
        {
            for (int j = 0; j < CircleSegments; j++)
            {
                uint i0 = baseIndex + (uint)(i * CircleSegments + j);
                uint i1 = baseIndex + (uint)(i * CircleSegments + (j + 1) % CircleSegments);
                uint i2 = baseIndex + (uint)((i + 1) * CircleSegments + j);
                uint i3 = baseIndex + (uint)((i + 1) * CircleSegments + (j + 1) % CircleSegments);

                indices.Add(i0);
                indices.Add(i2);
                indices.Add(i1);
                indices.Add(i1);
                indices.Add(i2);
                indices.Add(i3);
            }
        }
    }

    private static void CreateScaleAxis(Vector3 direction, Vector3 color, List<GizmoVertex> vertices, List<uint> indices)
    {
        uint baseIndex = (uint)vertices.Count;

        // Calculate perpendicular vectors
        var up = Math.Abs(Vector3.Dot(direction, Vector3.UnitY)) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        var right = Vector3.Normalize(Vector3.Cross(direction, up));
        up = Vector3.Normalize(Vector3.Cross(right, direction));

        // Create line (thin cylinder)
        for (int i = 0; i < CircleSegments; i++)
        {
            float angle = i * MathF.PI * 2 / CircleSegments;
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);

            var offset = right * cos * ArrowShaftRadius + up * sin * ArrowShaftRadius;

            vertices.Add(new GizmoVertex(offset, color));
            vertices.Add(new GizmoVertex(direction * ScaleLineLength + offset, color));
        }

        // Line indices
        for (int i = 0; i < CircleSegments; i++)
        {
            uint i0 = baseIndex + (uint)(i * 2);
            uint i1 = baseIndex + (uint)(i * 2 + 1);
            uint i2 = baseIndex + (uint)(((i + 1) % CircleSegments) * 2);
            uint i3 = baseIndex + (uint)(((i + 1) % CircleSegments) * 2 + 1);

            indices.Add(i0);
            indices.Add(i2);
            indices.Add(i1);
            indices.Add(i1);
            indices.Add(i2);
            indices.Add(i3);
        }

        // Create cube at the end
        CreateCube(direction * ScaleLineLength, ScaleCubeSize, color, vertices, indices);
    }

    private static void CreateCube(Vector3 center, float size, Vector3 color, List<GizmoVertex> vertices, List<uint> indices)
    {
        uint baseIndex = (uint)vertices.Count;
        float half = size * 0.5f;

        // 8 cube vertices
        Vector3[] cubeVerts =
        [
            center + new Vector3(-half, -half, half),
            center + new Vector3(half, -half, half),
            center + new Vector3(half, half, half),
            center + new Vector3(-half, half, half),
            center + new Vector3(half, -half, -half),
            center + new Vector3(-half, -half, -half),
            center + new Vector3(-half, half, -half),
            center + new Vector3(half, half, -half)
        ];

        foreach (var v in cubeVerts)
        {
            vertices.Add(new GizmoVertex(v, color));
        }

        // 12 triangles (36 indices)
        uint[] cubeIndices =
        [
            0, 2, 1, 0, 3, 2,
            4, 6, 5, 4, 7, 6,
            3, 7, 2, 3, 6, 7,
            5, 1, 4, 5, 0, 1,
            1, 7, 4, 1, 2, 7,
            5, 3, 0, 5, 6, 3
        ];

        foreach (var idx in cubeIndices)
        {
            indices.Add(baseIndex + idx);
        }
    }

    /// <summary>
    /// Creates a plane indicator quad for translation gizmo.
    /// The quad is positioned at (PlaneOffset, PlaneOffset) in the plane defined by axis1 and axis2.
    /// </summary>
    private static void CreatePlaneQuad(Vector3 axis1, Vector3 axis2, Vector3 color, List<GizmoVertex> vertices, List<uint> indices)
    {
        uint baseIndex = (uint)vertices.Count;

        // Create a small quad in the plane defined by axis1 and axis2
        // Positioned at offset from center
        var corner = axis1 * PlaneOffset + axis2 * PlaneOffset;
        var v0 = corner;
        var v1 = corner + axis1 * PlaneSize;
        var v2 = corner + axis1 * PlaneSize + axis2 * PlaneSize;
        var v3 = corner + axis2 * PlaneSize;

        vertices.Add(new GizmoVertex(v0, color));
        vertices.Add(new GizmoVertex(v1, color));
        vertices.Add(new GizmoVertex(v2, color));
        vertices.Add(new GizmoVertex(v3, color));

        // Two triangles for the quad (both sides for double-sided rendering)
        // Front face
        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 3);

        // Back face
        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 3);
        indices.Add(baseIndex + 2);
    }
}
