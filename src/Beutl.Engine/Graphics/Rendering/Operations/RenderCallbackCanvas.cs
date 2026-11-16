using System.Runtime.ExceptionServices;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class RenderCallbackCanvas
{
    private readonly RenderExecutionSessionToken _token;
    private readonly RenderPlacement _placement;
    private readonly PixelPoint _backingDeviceOrigin;
    private readonly Func<ImmediateCanvas> _openCanvas;
    private readonly CallbackCanvasCapability _capability;
    private readonly bool _mapLogicalOrigin;
    private bool _used;

    internal RenderCallbackCanvas(
        RenderExecutionSessionToken token,
        float density,
        Rect logicalBounds,
        PixelRect deviceBounds,
        Func<ImmediateCanvas> openCanvas,
        CallbackCanvasCapability capability,
        bool mapLogicalOrigin = true,
        PixelPoint? backingDeviceOrigin = null,
        Rect? rasterBounds = null)
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
        Rect raster = rasterBounds ?? deviceBounds.ToRect(density);
        _placement = RenderPlacement.FromRasterOrigin(logicalBounds, EffectiveScale.At(density), deviceBounds, raster.Position);
        ValidateDeviceBounds(_placement, raster);
        _backingDeviceOrigin = mapLogicalOrigin
            ? _placement.DeviceBounds.Position
            : backingDeviceOrigin ?? default;
        _openCanvas = openCanvas;
        _capability = capability;
        _mapLogicalOrigin = mapLogicalOrigin;
    }

    internal static RenderCallbackCanvas CreateTargetAttached(
        RenderExecutionSessionToken token,
        Rect logicalBounds,
        ImmediateCanvas destination,
        CallbackCanvasCapability capability)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Vector deviceGridOffset = DeviceGridAlignment.ResolveLogicalOffset(destination);
        PixelRect deviceBounds = PixelRect.FromRect(
            logicalBounds.Translate(deviceGridOffset),
            destination.Density);
        Rect rasterBounds = deviceBounds
            .ToRect(destination.Density)
            .Translate(-deviceGridOffset);
        return new RenderCallbackCanvas(
            token,
            destination.Density,
            logicalBounds,
            deviceBounds,
            destination.CreateExecutionView,
            capability,
            mapLogicalOrigin: false,
            backingDeviceOrigin: destination.DeviceOrigin,
            rasterBounds: rasterBounds);
    }

    public RenderPlacement Placement
    {
        get { _token.ThrowIfInactive(); return _placement; }
    }

    internal RenderExecutionSessionToken Token => _token;

    internal PixelPoint DeviceOriginUnchecked => _backingDeviceOrigin;

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
            // Every branch bounds the callback to its own footprint rather than describing content, so
            // each one rounds outward: the rect is stated in callback-local units, and a scope replayed
            // under a shrinking transform maps it to a sub-pixel span the device grid would snap away.
            if (_mapLogicalOrigin)
            {
                canvas.PushTransform(Matrix.CreateTranslation(-_placement.RasterBounds.X, -_placement.RasterBounds.Y));
                canvas.ClipRectCoveringDevicePixels(_placement.RasterBounds);
            }
            else if (_capability is CallbackCanvasCapability.TargetScope
                     or CallbackCanvasCapability.TargetCommandFull)
            {
                canvas.ClipRectCoveringDevicePixels(
                    RenderScaleUtilities.AddRasterApron(_placement.DeviceBounds)
                        .ToRect(_placement.Scale.Value)
                        .Translate(-_placement.DeviceGridOffset));
            }
            else
            {
                canvas.ClipRectCoveringDevicePixels(_placement.LogicalBounds);
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

    private static void ValidateDeviceBounds(RenderPlacement placement, Rect rasterBounds)
    {
        Rect bounds = placement.LogicalBounds;
        PixelRect deviceBounds = placement.DeviceBounds;
        float density = placement.Scale.Value;
        bool logicalBoundsAreEmpty = bounds.Width == 0 || bounds.Height == 0;
        if (deviceBounds.Width < 0
            || deviceBounds.Height < 0
            || (!logicalBoundsAreEmpty && (deviceBounds.Width == 0 || deviceBounds.Height == 0)))
        {
            throw new ArgumentException(
                "A non-empty callback canvas requires non-empty device bounds.",
                nameof(deviceBounds));
        }

        PixelRect semanticDeviceBounds = placement.ToDevice(bounds);
        if (!DeviceBoundsValidation.MatchesExtent(rasterBounds.Width, density, deviceBounds.Width)
            || !DeviceBoundsValidation.MatchesExtent(rasterBounds.Height, density, deviceBounds.Height)
            || !DeviceBoundsValidation.Covers(deviceBounds, semanticDeviceBounds))
        {
            throw new ArgumentException(
                "Callback canvas raster bounds must match the backing size and contain its logical bounds.",
                nameof(deviceBounds));
        }
    }
}
