using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics;

public partial class ImmediateCanvas
{
    /// <summary>How far a mapped coordinate may sit from a device-pixel boundary and still count as on it.</summary>
    private const float PixelAlignmentTolerance = 0.0001f;

    public void DrawSurface(SKSurface surface, Point point)
    {
        VerifyAccess();
        VerifyNativeTargetOperation();
        PrepareBlitPaint(antialias: true);

        using (SKImage image = surface.Snapshot())
            DrawTransferredImage(image, SKRect.Create(image.Width, image.Height),
                SKRect.Create(point.X, point.Y, image.Width, image.Height), GetPointBlitSampling());
        SurfaceSnapshot.Release(surface);

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
        PrepareBlitPaint(antialias: true);

        if (ColorTransferShader.IsRequired(renderTarget.ColorSpace, WorkingColorSpace))
        {
            using (SKImage image = renderTarget.Value.Snapshot())
                DrawTransferredImage(image, SKRect.Create(image.Width, image.Height),
                    SKRect.Create(point.X, point.Y, image.Width, image.Height), GetPointBlitSampling());
            SurfaceSnapshot.Release(renderTarget.Value);
        }
        else
        {
            // A snapshot of a surface that wraps a backend texture adds a copy task to Skia's graph,
            // which keeps it from merging render passes. Drawing the surface samples its texture directly.
            Canvas.DrawSurface(renderTarget.Value, point.X, point.Y, GetPointBlitSampling(), _sharedFillPaint);
        }

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
               && IsAxisAlignedAffine(transform)
            ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
            : s_compositeSampling;
    }

    /// <summary>
    /// Reports whether <paramref name="transform"/> only scales and translates: no rotation, shear or perspective,
    /// so the device axes stay parallel to the local ones.
    /// </summary>
    private static bool IsAxisAlignedAffine(in Matrix transform)
        => transform.M12 == 0
           && transform.M13 == 0
           && transform.M21 == 0
           && transform.M23 == 0
           && transform.M33 == 1;

    /// <summary>Resets the shared fill paint for a raw blit: no brush, the direct blend mode and the given edge mode.</summary>
    private void PrepareBlitPaint(bool antialias)
    {
        _sharedFillPaint.Reset();
        ApplyDirectBlendMode(_sharedFillPaint);
        _sharedFillPaint.IsAntialias = antialias;
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
            || !IsAxisAlignedAffine(destinationTransform))
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
        if (MathF.Abs(mappedOrigin.X - x) > PixelAlignmentTolerance
            || MathF.Abs(mappedOrigin.Y - y) > PixelAlignmentTolerance)
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

        PrepareBlitPaint(antialias: false);
        using (SKImage image = renderTarget.Value.Snapshot())
        using (PushDeviceSpace())
        {
            DrawTransferredImage(
                image,
                SKRect.Create(image.Width, image.Height),
                SKRect.Create(x, y, image.Width, image.Height),
                new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        }

        SurfaceSnapshot.Release(renderTarget.Value);
    }

    /// <summary>
    /// Maps <paramref name="dest"/> through the active transform and reports the device origin when the
    /// mapping lands a <paramref name="sourceSize"/> buffer on exact device pixels, so a copy is lossless.
    /// </summary>
    private bool TryGetLosslessDeviceOrigin(Rect dest, PixelSize sourceSize, out PixelPoint deviceOrigin)
    {
        deviceOrigin = default;
        Matrix transform = _currentTransform;
        if (!IsAxisAlignedAffine(transform))
        {
            return false;
        }

        Point mappedOrigin = dest.Position * transform;
        Point mappedFar = new Point(dest.Right, dest.Bottom) * transform;
        int x = (int)MathF.Round(mappedOrigin.X);
        int y = (int)MathF.Round(mappedOrigin.Y);
        if (MathF.Abs(mappedOrigin.X - x) > PixelAlignmentTolerance
            || MathF.Abs(mappedOrigin.Y - y) > PixelAlignmentTolerance
            || MathF.Abs(mappedFar.X - (x + sourceSize.Width)) > PixelAlignmentTolerance
            || MathF.Abs(mappedFar.Y - (y + sourceSize.Height)) > PixelAlignmentTolerance)
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
            using (SKImage image = renderTarget.Value.Snapshot())
                DrawImageScaled(image, dest);
            SurfaceSnapshot.Release(renderTarget.Value);
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
        PrepareBlitPaint(antialias: true);

        var src = SKRect.Create(image.Width, image.Height);
        DrawTransferredImage(image, src, dest.ToSKRect(), s_compositeSampling);
    }

    // Draw a surface into its own logical footprint (pixel size / density) at the given origin.
    public void DrawSurfaceScaled(SKSurface surface, Point origin, float scale)
    {
        VerifyAccess();
        VerifyNativeTargetOperation();
        PrepareBlitPaint(antialias: true);

        using (SKImage image = surface.Snapshot())
        {
            var src = SKRect.Create(image.Width, image.Height);
            var dest = SKRect.Create((float)origin.X, (float)origin.Y, image.Width / scale, image.Height / scale);
            DrawTransferredImage(image, src, dest, s_compositeSampling);
        }

        SurfaceSnapshot.Release(surface);

        if (!CanConsumeWithoutFlush(surface))
        {
            surface.Flush(true, true);
            RecordFlush(ImmediateCanvasFlushKind.SourceSurface);
        }
    }
}
