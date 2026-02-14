using System;
using SkiaSharp;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for 2D texture abstraction.
/// </summary>
public interface ITexture2D : IDisposable
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
    /// Gets the native handle of the texture (Vulkan Image handle).
    /// </summary>
    IntPtr NativeHandle { get; }

    /// <summary>
    /// Gets the native view handle of the texture (Vulkan ImageView handle).
    /// </summary>
    IntPtr NativeViewHandle { get; }

    /// <summary>
    /// Uploads pixel data to the texture.
    /// </summary>
    /// <param name="data">The pixel data to upload.</param>
    void Upload(ReadOnlySpan<byte> data);

    /// <summary>
    /// Downloads pixel data from the texture to a byte array.
    /// </summary>
    /// <returns>The pixel data.</returns>
    byte[] DownloadPixels();

    /// <summary>
    /// Creates a SkiaSharp surface that can render to this texture.
    /// </summary>
    /// <returns>A SkiaSharp surface.</returns>
    SKSurface CreateSkiaSurface();

    /// <summary>
    /// Prepares the texture for rendering (transitions to color attachment layout).
    /// </summary>
    void PrepareForRender();

    /// <summary>
    /// Prepares the texture for sampling (transitions to shader read-only layout).
    /// </summary>
    void PrepareForSampling();
}
