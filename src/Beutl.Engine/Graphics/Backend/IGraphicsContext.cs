using Beutl.Graphics3D;
using SkiaSharp;

namespace Beutl.Graphics.Backend;

internal interface IGraphicsContext : IDisposable
{
    GraphicsBackend Backend { get; }

    GRContext SkiaContext { get; }

    /// <summary>
    /// Gets a value indicating whether 3D rendering is supported.
    /// </summary>
    bool Supports3DRendering { get; }

    ISharedTexture CreateTexture(int width, int height, TextureFormat format);

    /// <summary>
    /// Creates a new 3D renderer.
    /// </summary>
    /// <returns>A new 3D renderer instance.</returns>
    /// <exception cref="NotSupportedException">Thrown if 3D rendering is not supported.</exception>
    I3DRenderer Create3DRenderer();

    void WaitIdle();
}
