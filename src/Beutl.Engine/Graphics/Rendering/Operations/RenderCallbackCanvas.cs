using System.Runtime.ExceptionServices;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class RenderCallbackCanvas
{
    private readonly RenderExecutionSessionToken _token;
    private readonly float _density;
    private readonly Rect _logicalBounds;
    private readonly Point _logicalOrigin;
    private readonly PixelRect _deviceBounds;
    private readonly Rect _rasterBounds;
    private readonly Func<ImmediateCanvas> _openCanvas;
    private readonly CallbackCanvasCapability _capability;
    private readonly bool _mapLogicalOrigin;
    private bool _used;

    internal RenderCallbackCanvas(
        RenderExecutionSessionToken token,
        float density,
        Rect logicalBounds,
        Func<ImmediateCanvas> openCanvas,
        CallbackCanvasCapability capability,
        bool mapLogicalOrigin = true)
        : this(
            token,
            density,
            logicalBounds,
            PixelRect.FromRect(logicalBounds, density),
            openCanvas,
            capability,
            mapLogicalOrigin)
    {
    }

    internal RenderCallbackCanvas(
        RenderExecutionSessionToken token,
        float density,
        Rect logicalBounds,
        PixelRect deviceBounds,
        Func<ImmediateCanvas> openCanvas,
        CallbackCanvasCapability capability,
        bool mapLogicalOrigin = true)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!float.IsFinite(density) || density <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(density), density, "Callback canvas density must be positive and finite.");
        }

        RenderRectValidation.ThrowIfInvalidInput(logicalBounds, nameof(logicalBounds));
        ArgumentNullException.ThrowIfNull(openCanvas);
        if (!Enum.IsDefined(capability))
            throw new ArgumentOutOfRangeException(nameof(capability), capability, "The callback capability is invalid.");

        _token = token;
        _density = density;
        _logicalBounds = logicalBounds;
        _deviceBounds = ValidateDeviceBounds(logicalBounds, density, deviceBounds);
        _rasterBounds = _deviceBounds.ToRect(density);
        _logicalOrigin = new Point(_deviceBounds.X / density, _deviceBounds.Y / density);
        _openCanvas = openCanvas;
        _capability = capability;
        _mapLogicalOrigin = mapLogicalOrigin;
    }

    public float Density
    {
        get { _token.ThrowIfInactive(); return _density; }
    }

    public Rect LogicalBounds
    {
        get { _token.ThrowIfInactive(); return _logicalBounds; }
    }

    public Point LogicalOrigin
    {
        get { _token.ThrowIfInactive(); return _logicalOrigin; }
    }

    public PixelRect DeviceBounds
    {
        get { _token.ThrowIfInactive(); return _deviceBounds; }
    }

    /// <summary>
    /// Gets the pixel-aligned logical footprint of the backing target. The footprint can
    /// conservatively extend beyond <see cref="LogicalBounds"/> because of device-pixel rounding.
    /// </summary>
    public Rect RasterBounds
    {
        get { _token.ThrowIfInactive(); return _rasterBounds; }
    }

    internal PixelRect DeviceBoundsUnchecked => _deviceBounds;

    public void Use(Action<ImmediateCanvas> draw)
    {
        _token.ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(draw);
        if (_used)
            throw new InvalidOperationException("A callback canvas facade can be used only once.");

        _used = true;
        ImmediateCanvas canvas = _openCanvas()
            ?? throw new InvalidOperationException("The callback canvas provider returned null.");
        ExceptionDispatchInfo? primaryFailure = null;
        bool entered = false;
        try
        {
            _token.EnterCanvas(canvas, this);
            entered = true;
            canvas.ConfigureExecutionCallback(_token, _capability);
            if (_mapLogicalOrigin)
            {
                canvas.PushTransform(Matrix.CreateTranslation(-_logicalOrigin.X, -_logicalOrigin.Y));
                canvas.ClipRect(_rasterBounds);
            }
            else if (_capability == CallbackCanvasCapability.TargetScope)
            {
                canvas.ClipRect(
                    RenderScaleUtilities.AddRasterApron(_deviceBounds).ToRect(_density));
            }
            else
            {
                canvas.ClipRect(_logicalBounds);
            }
            canvas.PinExecutionCallbackState();
            draw(canvas);
        }
        catch (Exception ex)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            try
            {
                canvas.CloseWithoutFlush();
            }
            catch when (primaryFailure is not null)
            {
                // The callback failure remains primary; canvas cleanup is best-effort on this path.
            }
            finally
            {
                if (entered)
                    _token.ExitCanvas(canvas);
            }
        }

        primaryFailure?.Throw();
    }

    private static PixelRect ValidateDeviceBounds(Rect bounds, float density, PixelRect deviceBounds)
    {
        bool logicalBoundsAreEmpty = bounds.Width == 0 || bounds.Height == 0;
        if (deviceBounds.Width < 0
            || deviceBounds.Height < 0
            || (!logicalBoundsAreEmpty && (deviceBounds.Width == 0 || deviceBounds.Height == 0)))
        {
            throw new ArgumentException(
                "A non-empty callback canvas requires non-empty device bounds.",
                nameof(deviceBounds));
        }

        PixelRect semanticDeviceBounds = PixelRect.FromRect(bounds, density);
        if (deviceBounds.X > semanticDeviceBounds.X
            || deviceBounds.Y > semanticDeviceBounds.Y
            || deviceBounds.Right < semanticDeviceBounds.Right
            || deviceBounds.Bottom < semanticDeviceBounds.Bottom)
        {
            throw new ArgumentException(
                "Callback canvas device bounds must contain its logical bounds at the active density.",
                nameof(deviceBounds));
        }

        return deviceBounds;
    }
}

internal enum CallbackCanvasCapability : byte
{
    Draw,
    TargetScope,
    TargetCommandFull,
    TargetCommandRegion,
    TargetCommandEmpty,
}
