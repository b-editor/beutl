using System;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for 2D texture abstraction.
/// </summary>
internal interface ITexture2D : IDisposable
{
    /// <summary>
    /// Gets the width of the texture.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the height of the texture.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Gets the format of the texture.
    /// </summary>
    TextureFormat Format { get; }

    /// <summary>
    /// Gets the native handle of the texture.
    /// </summary>
    IntPtr NativeHandle { get; }

    /// <summary>
    /// Uploads pixel data to the texture.
    /// </summary>
    /// <param name="data">The pixel data to upload.</param>
    void Upload(ReadOnlySpan<byte> data);
}
