using System;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Backend;

namespace Beutl.Graphics3D.Meshes;

/// <summary>
/// Abstract base class for 3D mesh geometry.
/// </summary>
public abstract partial class Mesh : EngineObject
{
    /// <summary>
    /// Applies the mesh geometry to the resource.
    /// </summary>
    /// <param name="resource">The resource to apply to.</param>
    /// <param name="vertices">Output array of vertices.</param>
    /// <param name="indices">Output array of indices.</param>
    public abstract void ApplyTo(Resource resource, out Vertex3D[] vertices, out uint[] indices);

    public partial class Resource
    {
        private int? _capturedVersion;
        private Vertex3D[]? _cachedVertices;
        private uint[]? _cachedIndices;

        /// <summary>
        /// Gets or sets the vertex buffer. Set by Renderer3D.
        /// </summary>
        internal IBuffer? VertexBuffer { get; set; }

        /// <summary>
        /// Gets or sets the index buffer. Set by Renderer3D.
        /// </summary>
        internal IBuffer? IndexBuffer { get; set; }

        /// <summary>
        /// Gets or sets whether the GPU buffers need to be recreated.
        /// </summary>
        internal bool BuffersDirty { get; set; } = true;

        /// <summary>
        /// Gets the cached vertices, regenerating if needed.
        /// </summary>
        public ReadOnlySpan<Vertex3D> GetVertices()
        {
            EnsureCached();
            return _cachedVertices;
        }

        /// <summary>
        /// Gets the cached indices, regenerating if needed.
        /// </summary>
        public ReadOnlySpan<uint> GetIndices()
        {
            EnsureCached();
            return _cachedIndices;
        }

        /// <summary>
        /// Gets the number of vertices.
        /// </summary>
        public int VertexCount
        {
            get
            {
                EnsureCached();
                return _cachedVertices?.Length ?? 0;
            }
        }

        /// <summary>
        /// Gets the number of indices.
        /// </summary>
        public int IndexCount
        {
            get
            {
                EnsureCached();
                return _cachedIndices?.Length ?? 0;
            }
        }

        /// <summary>
        /// Gets the bounding box of the mesh.
        /// </summary>
        public BoundingBox GetBoundingBox()
        {
            EnsureCached();

            if (_cachedVertices == null || _cachedVertices.Length == 0)
                return new BoundingBox(Vector3.Zero, Vector3.Zero);

            var min = _cachedVertices[0].Position;
            var max = _cachedVertices[0].Position;

            foreach (var vertex in _cachedVertices)
            {
                min = Vector3.Min(min, vertex.Position);
                max = Vector3.Max(max, vertex.Position);
            }

            return new BoundingBox(min, max);
        }

        private void EnsureCached()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);

            if (_capturedVersion != Version || _cachedVertices == null)
            {
                _capturedVersion = Version;
                BuffersDirty = true;
                GetOriginal().ApplyTo(this, out _cachedVertices!, out _cachedIndices!);
            }
        }

        partial void PostDispose(bool disposing)
        {
            VertexBuffer?.Dispose();
            VertexBuffer = null;
            IndexBuffer?.Dispose();
            IndexBuffer = null;
            _cachedVertices = null;
            _cachedIndices = null;
        }
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
