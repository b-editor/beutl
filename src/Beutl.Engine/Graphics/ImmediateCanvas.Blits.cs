using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics;

public partial class ImmediateCanvas
{
    public void DrawSurface(SKSurface surface, Point point)
    {
        VerifyAccess();
        VerifyNativeTargetOperation();
        _sharedFillPaint.Reset();
        ApplyDirectBlendMode(_sharedFillPaint);
        _sharedFillPaint.IsAntialias = true;

        Canvas.DrawSurface(surface, point.X, point.Y, GetPointBlitSampling(), _sharedFillPaint);

        if (!CanConsumeWithoutFlush(surface))
        {
            surface.Flush(true, true);
            RecordFlush(ImmediateCanvasFlushKind.SourceSurface);
        }
    }

    public void DrawRenderTarget(RenderTarget renderTarget, Point point)
    {
        VerifyAccess();
        VerifyNativeTargetOperation();
        // NOTE: renderTargetを保持しておいて次回Flushされたときに開放すると効率的
        renderTarget.VerifyAccess();
        renderTarget.PrepareBackendForSkiaSampling();
        _sharedFillPaint.Reset();
        ApplyDirectBlendMode(_sharedFillPaint);
        _sharedFillPaint.IsAntialias = true;

        Canvas.DrawSurface(renderTarget.Value, point.X, point.Y, GetPointBlitSampling(), _sharedFillPaint);

        if (!CanConsumeWithoutFlush(renderTarget))
        {
            renderTarget.Value.Flush(true, true);
            RecordFlush(ImmediateCanvasFlushKind.SourceSurface);
        }
    }

    // A point blit copies pixels only while the transform keeps the device axes at unit scale, flipped or not.
    // Under any other scale or a rotation, nearest sampling drops and duplicates source pixels, so the blit
    // resamples like a scaled draw; a fractional offset still lands on the pixel grid, as it always has.
    private SKSamplingOptions GetPointBlitSampling()
    {
        Matrix transform = _currentTransform;
        return MathF.Abs(transform.M11) == 1f
               && MathF.Abs(transform.M22) == 1f
               && transform.M12 == 0
               && transform.M21 == 0
               && transform.M13 == 0
               && transform.M23 == 0
               && transform.M33 == 1
            ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
            : s_compositeSampling;
    }

    public void DrawRenderTargetScaled(RenderTarget renderTarget, Rect dest)
        => DrawRenderTargetScaledCore(
            renderTarget,
            dest,
            flushSource: !CanConsumeWithoutFlush(renderTarget));

    internal void DrawRenderTargetScaledWithoutFlush(RenderTarget renderTarget, Rect dest)
        => DrawRenderTargetScaledCore(renderTarget, dest, flushSource: false);

    private bool CanConsumeWithoutFlush(RenderTarget renderTarget)
    {
        renderTarget.VerifyAccess();
        return CanConsumeWithoutFlush(renderTarget.RawValue);
    }

    private bool CanConsumeWithoutFlush(SKSurface surface)
    {
        if (!_allowDeferredSameContextSampling || _flushOnDispose)
            return false;

        GRRecordingContext? destinationContext = _renderTarget.RawValue.Context;
        GRRecordingContext? sourceContext = surface.Context;
        return destinationContext is null
            ? sourceContext is null
            : sourceContext is not null && destinationContext.Handle == sourceContext.Handle;
    }

    internal bool CanDrawPixelAligned(
        Rect dest,
        float sourceDensity,
        PixelSize sourceSize)
    {
        VerifyAccess();
        VerifyNativeTargetOperation();
        return TryGetPixelAlignedDeviceOrigin(
            dest,
            sourceDensity,
            sourceSize,
            _currentDensity,
            _currentTransform,
            out _);
    }

    internal static bool CanDrawPixelAligned(
        Rect dest,
        float sourceDensity,
        PixelSize sourceSize,
        float destinationDensity,
        Matrix destinationTransform)
        => TryGetPixelAlignedDeviceOrigin(
            dest,
            sourceDensity,
            sourceSize,
            destinationDensity,
            destinationTransform,
            out _);

    private static bool TryGetPixelAlignedDeviceOrigin(
        Rect dest,
        float sourceDensity,
        PixelSize sourceSize,
        float destinationDensity,
        Matrix destinationTransform,
        out PixelPoint deviceOrigin)
    {
        deviceOrigin = default;
        if (destinationDensity != sourceDensity
            || destinationTransform.M11 != sourceDensity
            || destinationTransform.M22 != sourceDensity
            || destinationTransform.M12 != 0
            || destinationTransform.M13 != 0
            || destinationTransform.M21 != 0
            || destinationTransform.M23 != 0
            || destinationTransform.M33 != 1)
        {
            return false;
        }

        PixelRect deviceBounds = PixelRect.FromRect(dest, sourceDensity);
        if (deviceBounds.Size != sourceSize
            || deviceBounds.ToRect(sourceDensity) != dest)
        {
            return false;
        }

        Point mappedOrigin = dest.Position * destinationTransform;
        int x = (int)MathF.Round(mappedOrigin.X);
        int y = (int)MathF.Round(mappedOrigin.Y);
        if (MathF.Abs(mappedOrigin.X - x) > 0.0001f
            || MathF.Abs(mappedOrigin.Y - y) > 0.0001f)
        {
            return false;
        }

        deviceOrigin = new PixelPoint(x, y);
        return true;
    }

    /// <summary>
    /// Reports whether a <paramref name="sourceSize"/> buffer drawn at <paramref name="dest"/> lands on
    /// exact device pixels, so a nearest-sampled point blit reproduces it instead of snapping it.
    /// </summary>
    internal bool CanBlitLossless(Rect dest, PixelSize sourceSize)
    {
        VerifyAccess();
        VerifyNativeTargetOperation();
        return TryGetLosslessDeviceOrigin(dest, sourceSize, out _);
    }

    internal void DrawRenderTargetPixelsWithoutFlush(RenderTarget renderTarget, int x, int y)
    {
        VerifyAccess();
        VerifyNativeTargetOperation();
        renderTarget.VerifyAccess();
        renderTarget.PrepareBackendForSkiaSampling();

        using SKImage image = renderTarget.Value.Snapshot();
        _sharedFillPaint.Reset();
        ApplyDirectBlendMode(_sharedFillPaint);
        _sharedFillPaint.IsAntialias = false;
        var source = SKRect.Create(image.Width, image.Height);
        var destination = SKRect.Create(x, y, image.Width, image.Height);
        using (PushDeviceSpace())
        {
            Canvas.DrawImage(
                image,
                source,
                destination,
                new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
                _sharedFillPaint);
        }
    }

    /// <summary>
    /// Maps <paramref name="dest"/> through the active transform and reports the device origin when the
    /// mapping lands a <paramref name="sourceSize"/> buffer on exact device pixels, so a copy is lossless.
    /// </summary>
    private bool TryGetLosslessDeviceOrigin(Rect dest, PixelSize sourceSize, out PixelPoint deviceOrigin)
    {
        deviceOrigin = default;
        Matrix transform = _currentTransform;
        if (transform.M12 != 0
            || transform.M13 != 0
            || transform.M21 != 0
            || transform.M23 != 0
            || transform.M33 != 1)
        {
            return false;
        }

        Point mappedOrigin = dest.Position * transform;
        Point mappedFar = new Point(dest.Right, dest.Bottom) * transform;
        int x = (int)MathF.Round(mappedOrigin.X);
        int y = (int)MathF.Round(mappedOrigin.Y);
        if (MathF.Abs(mappedOrigin.X - x) > 0.0001f
            || MathF.Abs(mappedOrigin.Y - y) > 0.0001f
            || MathF.Abs(mappedFar.X - (x + sourceSize.Width)) > 0.0001f
            || MathF.Abs(mappedFar.Y - (y + sourceSize.Height)) > 0.0001f)
        {
            return false;
        }

        deviceOrigin = new PixelPoint(x, y);
        return true;
    }

    private void DrawRenderTargetScaledCore(RenderTarget renderTarget, Rect dest, bool flushSource)
    {
        VerifyAccess();
        VerifyNativeTargetOperation();
        renderTarget.VerifyAccess();
        renderTarget.PrepareBackendForSkiaSampling();

        // Resampling a buffer that already lands on exact device pixels only softens and rings it.
        if (TryGetLosslessDeviceOrigin(
                dest,
                new PixelSize(renderTarget.Width, renderTarget.Height),
                out PixelPoint deviceOrigin))
        {
            DrawRenderTargetPixelsWithoutFlush(renderTarget, deviceOrigin.X, deviceOrigin.Y);
        }
        else
        {
            using SKImage image = renderTarget.Value.Snapshot();
            DrawImageScaled(image, dest);
        }

        if (flushSource)
        {
            renderTarget.Value.Flush(true, true);
            RecordFlush(ImmediateCanvasFlushKind.SourceSurface);
        }
    }

    public void DrawImageScaled(SKImage image, Rect dest)
    {
        VerifyPixelOperation();
        VerifyCallbackResource(image, nameof(image));
        _sharedFillPaint.Reset();
        ApplyDirectBlendMode(_sharedFillPaint);
        _sharedFillPaint.IsAntialias = true;

        var src = SKRect.Create(image.Width, image.Height);
        Canvas.DrawImage(image, src, dest.ToSKRect(), s_compositeSampling, _sharedFillPaint);
    }

    // Draw a surface into its own logical footprint (pixel size / density) at the given origin.
    public void DrawSurfaceScaled(SKSurface surface, Point origin, float scale)
    {
        VerifyAccess();
        VerifyNativeTargetOperation();
        _sharedFillPaint.Reset();
        ApplyDirectBlendMode(_sharedFillPaint);
        _sharedFillPaint.IsAntialias = true;

        using SKImage image = surface.Snapshot();
        var src = SKRect.Create(image.Width, image.Height);
        var dest = SKRect.Create((float)origin.X, (float)origin.Y, image.Width / scale, image.Height / scale);
        Canvas.DrawImage(image, src, dest, s_compositeSampling, _sharedFillPaint);

        if (!CanConsumeWithoutFlush(surface))
        {
            surface.Flush(true, true);
            RecordFlush(ImmediateCanvasFlushKind.SourceSurface);
        }
    }
}
