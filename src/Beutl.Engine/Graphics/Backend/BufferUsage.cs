using System;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Specifies how a buffer will be used.
/// </summary>
[Flags]
public enum BufferUsage
{
    /// <summary>
    /// No specific usage.
    /// </summary>
    None = 0,

    /// <summary>
    /// Buffer can be used as a vertex buffer.
    /// </summary>
    VertexBuffer = 1 << 0,

    /// <summary>
    /// Buffer can be used as an index buffer.
    /// </summary>
    IndexBuffer = 1 << 1,

    /// <summary>
    /// Buffer can be used as a uniform buffer.
    /// </summary>
    UniformBuffer = 1 << 2,

    /// <summary>
    /// Buffer can be used as a storage buffer.
    /// </summary>
    StorageBuffer = 1 << 3,

    /// <summary>
    /// Buffer can be used as a source for transfer operations.
    /// </summary>
    TransferSource = 1 << 4,

    /// <summary>
    /// Buffer can be used as a destination for transfer operations.
    /// </summary>
    TransferDestination = 1 << 5,

    /// <summary>
    /// Buffer can be used as an indirect buffer.
    /// </summary>
    IndirectBuffer = 1 << 6,
}
