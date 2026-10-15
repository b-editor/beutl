using System;

namespace Beutl.Graphics.Backend;

public interface IBuffer : IDisposable
{
    /// <summary>
    /// Gets the size of the buffer in bytes.
    /// </summary>
    ulong Size { get; }

    void Upload<T>(ReadOnlySpan<T> data) where T : unmanaged;
}
