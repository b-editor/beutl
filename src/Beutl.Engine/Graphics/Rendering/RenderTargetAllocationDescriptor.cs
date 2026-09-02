using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>Describes one renderer-owned target allocation.</summary>
public readonly record struct RenderTargetAllocationDescriptor
{
    internal RenderTargetAllocationDescriptor(
        PixelSize deviceSize,
        GRRecordingContext? graphicsContext,
        nint? graphicsContextHandle)
    {
        DeviceSize = deviceSize;
        GraphicsContext = graphicsContext;
        GraphicsContextHandle = graphicsContextHandle;
    }

    /// <summary>Gets the exact positive device-pixel size.</summary>
    public PixelSize DeviceSize { get; }

    /// <summary>Gets the required pixel format.</summary>
    public RenderTargetPixelFormat PixelFormat =>
        RenderTargetPixelFormat.LinearPremultipliedRgba16Float;

    /// <summary>
    /// Gets the borrowed Skia context for a context-bound GPU request, or <see langword="null"/> for a
    /// CPU request or a target-less request whose backend is not bound yet.
    /// </summary>
    /// <remarks>
    /// The factory may use this value only for the duration of
    /// <see cref="IRenderTargetFactory.Create"/>.
    /// </remarks>
    public GRRecordingContext? GraphicsContext { get; }

    /// <summary>
    /// Gets the required Skia context handle: a positive value for GPU, zero for CPU, or
    /// <see langword="null"/> when a target-less request has not bound a backend yet.
    /// </summary>
    public nint? GraphicsContextHandle { get; }

    /// <summary>Gets the required GPU backend, or <see langword="null"/> when no GPU context is bound.</summary>
    public GRBackend? GraphicsBackend => GraphicsContext?.Backend;
}
