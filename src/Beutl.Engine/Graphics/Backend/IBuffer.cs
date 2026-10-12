using System;

namespace Beutl.Graphics.Backend;

public interface IBuffer : IDisposable
{
    /// <summary>
    /// Gets the size of the buffer in bytes.
    /// </summary>
    ulong Size { get; }

    void Upload<T>(ReadOnlySpan<T> data) where T : unmanaged;

    /// <summary>
    /// Maps the buffer memory for CPU access.
    /// </summary>
    /// <returns>A pointer to the mapped memory.</returns>
    IntPtr Map();

    void Unmap();
}
