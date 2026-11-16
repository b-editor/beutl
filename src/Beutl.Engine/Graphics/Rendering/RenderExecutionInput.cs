using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public sealed class RenderExecutionInput
{
    private readonly RenderExecutionSessionToken _token;
    private readonly RenderPlacement _placement;
    private readonly SKImage _image;
    private readonly Func<Bitmap>? _createSnapshot;
    private bool _snapshotUsed;

    internal RenderExecutionInput(
        RenderExecutionSessionToken token,
        Rect bounds,
        EffectiveScale effectiveScale,
        PixelRect deviceBounds,
        Rect rasterBounds,
        SKImage image,
        Func<Bitmap>? createSnapshot)
    {
        ArgumentNullException.ThrowIfNull(token);
        RenderDescriptionValidation.ThrowUnlessFiniteNonEmpty(bounds, nameof(bounds));
        if (effectiveScale.IsUnbounded)
        {
            throw new ArgumentException(
                "An execution input requires a concrete effective scale.",
                nameof(effectiveScale));
        }

        ArgumentNullException.ThrowIfNull(image);

        _token = token;
        ValidateDeviceBounds(bounds, effectiveScale.Value, deviceBounds, rasterBounds);
        _placement = RenderPlacement.FromRasterOrigin(bounds, effectiveScale, deviceBounds, rasterBounds.Position);
        _image = image;
        _createSnapshot = createSnapshot;
    }

    public RenderPlacement Placement
    {
        get { _token.ThrowIfInactive(); return _placement; }
    }

    public void Draw(ImmediateCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        _token.VerifyActiveCanvas(canvas);
        canvas.DrawExecutionInput(_image, _placement.RasterBounds);
    }

    /// <summary>
    /// Draws the input's pixels through <paramref name="paint"/> and <paramref name="sampling"/>, so a
    /// caller can modulate them -- with a colour filter, an alpha, an image filter -- and choose how
    /// they are resampled, inside the same draw.
    /// </summary>
    /// <remarks>
    /// Filling a rectangle with the input's shader instead leaves the caller to resample it through a
    /// tile mode, which is a poor substitute: the shader is point-sampled, so a minified input reduces
    /// to whichever texels the sample points happen to hit, and a decal domain narrower than the
    /// sample footprint drops out altogether -- the input disappears.
    /// </remarks>
    public void Draw(ImmediateCanvas canvas, SKPaint paint, SKSamplingOptions sampling)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(paint);
        _token.VerifyActiveCanvas(canvas);
        canvas.DrawExecutionInput(_image, _placement.RasterBounds, paint, sampling);
    }

    public void DrawDeviceSpace(ImmediateCanvas canvas, Point devicePoint)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (!float.IsFinite(devicePoint.X) || !float.IsFinite(devicePoint.Y))
            throw new ArgumentException("The device-space point must be finite.", nameof(devicePoint));

        PixelPoint canvasOrigin = _token.GetActiveCanvasDeviceOrigin(canvas);
        canvas.DrawExecutionInputDeviceSpace(
            _image,
            new Point(devicePoint.X - canvasOrigin.X, devicePoint.Y - canvasOrigin.Y));
    }

    public void UseSnapshot(Action<Bitmap> use)
    {
        _token.ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(use);
        if (_createSnapshot is null)
            throw new InvalidOperationException("CPU readback was not declared for this execution input.");
        if (_snapshotUsed)
            throw new InvalidOperationException("An execution input snapshot is a one-shot lease.");

        _snapshotUsed = true;
        using Bitmap snapshot = _createSnapshot()
            ?? throw new InvalidOperationException("The input snapshot provider returned null.");
        _token.AuthorizeResource(snapshot, () => use(snapshot));
    }

    private static void ValidateDeviceBounds(
        Rect bounds,
        float density,
        PixelRect deviceBounds,
        Rect rasterBounds)
    {
        if (deviceBounds.Width <= 0 || deviceBounds.Height <= 0)
        {
            throw new ArgumentException(
                "An execution input requires non-empty device bounds.",
                nameof(deviceBounds));
        }

        if (!DeviceBoundsValidation.MatchesExtent(rasterBounds.Width, density, deviceBounds.Width)
            || !DeviceBoundsValidation.MatchesExtent(rasterBounds.Height, density, deviceBounds.Height)
            || rasterBounds.X > bounds.X
            || rasterBounds.Y > bounds.Y
            || rasterBounds.Right < bounds.Right
            || rasterBounds.Bottom < bounds.Bottom)
        {
            throw new ArgumentException(
                "Execution input raster bounds must match the backing size and contain the semantic bounds.",
                nameof(deviceBounds));
        }
    }
}
