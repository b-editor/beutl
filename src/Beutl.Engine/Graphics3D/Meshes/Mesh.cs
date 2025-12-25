using System;
using System.Numerics;
using Beutl.Graphics.Backend.Vulkan3D;

namespace Beutl.Graphics3D.Meshes;

/// <summary>
/// Represents 3D mesh data with vertices and indices.
/// </summary>
public class Mesh : IDisposable
{
    private Vertex3D[] _vertices;
    private uint[] _indices;
    private bool _disposed;

    public Mesh(Vertex3D[] vertices, uint[] indices)
    {
        _vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
        _indices = indices ?? throw new ArgumentNullException(nameof(indices));
    }

    /// <summary>
    /// Gets the vertices of the mesh.
    /// </summary>
    public ReadOnlySpan<Vertex3D> Vertices => _vertices;

    /// <summary>
    /// Gets the indices of the mesh.
    /// </summary>
    public ReadOnlySpan<uint> Indices => _indices;

    /// <summary>
    /// Gets the number of vertices.
    /// </summary>
    public int VertexCount => _vertices.Length;

    /// <summary>
    /// Gets the number of indices.
    /// </summary>
    public int IndexCount => _indices.Length;

    /// <summary>
    /// Gets the bounding box of the mesh.
    /// </summary>
    public BoundingBox GetBoundingBox()
    {
        if (_vertices.Length == 0)
            return new BoundingBox(Vector3.Zero, Vector3.Zero);

        var min = _vertices[0].Position;
        var max = _vertices[0].Position;

        foreach (var vertex in _vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }

        return new BoundingBox(min, max);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vertices = [];
        _indices = [];
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents an axis-aligned bounding box.
/// </summary>
public readonly struct BoundingBox
{
    public BoundingBox(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    public Vector3 Min { get; }
    public Vector3 Max { get; }

    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;
}
