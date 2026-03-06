using System;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for GPU buffer abstraction.
/// </summary>
public interface IBuffer : IDisposable
{
    /// <summary>
    /// Gets the size of the buffer in bytes.
    /// </summary>
    ulong Size { get; }

    /// <summary>
    /// Uploads data to the buffer.
    /// </summary>
    /// <typeparam name="T">The type of data to upload.</typeparam>
    /// <param name="data">The data to upload.</param>
    void Upload<T>(ReadOnlySpan<T> data) where T : unmanaged;

    /// <summary>
    /// Maps the buffer memory for CPU access.
    /// </summary>
    /// <returns>A pointer to the mapped memory.</returns>
    IntPtr Map();

    /// <summary>
    /// Unmaps the buffer memory.
    /// </summary>
    void Unmap();
}
