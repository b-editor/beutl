using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public sealed class RenderExecutionInput
{
    private readonly RenderExecutionSessionToken _token;
    private readonly Rect _bounds;
    private readonly EffectiveScale _effectiveScale;
    private readonly PixelRect _deviceBounds;
    private readonly Rect _rasterBounds;
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
        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(bounds, nameof(bounds));
        if (effectiveScale.IsUnbounded)
        {
            throw new ArgumentException(
                "An execution input requires a concrete effective scale.",
                nameof(effectiveScale));
        }

        ArgumentNullException.ThrowIfNull(image);

        _token = token;
        _bounds = bounds;
        _effectiveScale = effectiveScale;
        _deviceBounds = ValidateDeviceBounds(
            bounds,
            effectiveScale.Value,
            deviceBounds,
            rasterBounds);
        _rasterBounds = rasterBounds;
        _image = image;
        _createSnapshot = createSnapshot;
    }

    public Rect Bounds
    {
        get { _token.ThrowIfInactive(); return _bounds; }
    }

    public EffectiveScale EffectiveScale
    {
        get { _token.ThrowIfInactive(); return _effectiveScale; }
    }

    public PixelRect DeviceBounds
    {
        get { _token.ThrowIfInactive(); return _deviceBounds; }
    }

    public PixelSize DeviceSize
    {
        get { _token.ThrowIfInactive(); return _deviceBounds.Size; }
    }

    /// <summary>
    /// Gets the translation from input-local coordinates to the composition-device grid used to
    /// round <see cref="DeviceBounds"/>.
    /// </summary>
    public Vector DeviceGridOffset
    {
        get
        {
            _token.ThrowIfInactive();
            return new Vector(
                (_deviceBounds.X / _effectiveScale.Value) - _rasterBounds.X,
                (_deviceBounds.Y / _effectiveScale.Value) - _rasterBounds.Y);
        }
    }

    /// <summary>
    /// Gets the pixel-aligned logical footprint represented by the complete backing image.
    /// This can conservatively extend beyond <see cref="Bounds"/> because of device-pixel rounding.
    /// </summary>
    public Rect RasterBounds
    {
        get { _token.ThrowIfInactive(); return _rasterBounds; }
    }

    public Point LogicalOrigin
    {
        get
        {
            _token.ThrowIfInactive();
            return _rasterBounds.Position;
        }
    }

    public void Draw(ImmediateCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        _token.VerifyActiveCanvas(canvas);
        canvas.DrawExecutionInput(_image, _rasterBounds);
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
        canvas.DrawExecutionInput(_image, _rasterBounds, paint, sampling);
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

    private static PixelRect ValidateDeviceBounds(
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

        return deviceBounds;
    }
}
