using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal static class TriangleWarpRenderer
{
    public static bool Draw(
        Rect warpBounds,
        ReadOnlySpan<Point> rest,
        ReadOnlySpan<Point> deformed,
        ushort[] indices,
        CustomFilterEffectContext context,
        EffectTarget target,
        EffectTarget output,
        ILogger logger)
    {
        RenderTarget source = target.RenderTarget!;
        source.PrepareForSampling(
            RenderTargetSamplingIntent.SameContextTextureSampling(output.RenderTarget!.RawValue.Context));
        using SurfaceSnapshot.Lease snapshot = SurfaceSnapshot.Take(source.Value);

        RenderPlacement src = target.Placement;
        RenderPlacement dst = output.Placement;
        float srcScale = src.Scale.Value;
        float dstScale = dst.Scale.Value;
        SKSamplingOptions sampling = RasterShaderMapping.SamplingFor(srcScale, dstScale);
        using SKShader? shader = CreateSourceShader(
            snapshot.Image, source.RawValue.Context, target.Bounds, src, sampling, out Rect srcRasterBounds);
        if (shader is null)
        {
            CustomFilterEffectContext.ThrowIfDeliveryReadbackFailure(context.Intent, src.DeviceBounds);
            context.RenderTargetLeaseSession?.MarkContentDropped();
            logger.LogWarning("Could not sample the source surface; the preview keeps the source pixels.");
            return false;
        }

        var texs = new SKPoint[rest.Length];
        var positions = new SKPoint[rest.Length];
        Point srcOrigin = target.Bounds.Position - srcRasterBounds.Position;
        Point dstOrigin = warpBounds.Position - dst.RasterBounds.Position;
        for (int i = 0; i < positions.Length; i++)
        {
            texs[i] = ToDevice(rest[i], target.Bounds.Size, srcOrigin, srcScale);
            positions[i] = ToDevice(deformed[i], warpBounds.Size, dstOrigin, dstScale);
        }

        using SKVertices vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, texs, colors: null, indices);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        using ImmediateCanvas canvas = context.Open(output);
        using (canvas.PushDeviceSpace())
        {
            canvas.Clear();
            canvas.Canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
        }
        return true;

        static SKPoint ToDevice(Point point, Size size, Point origin, float scale)
            => new((float)((origin.X + (double)point.X * size.Width) * scale),
                (float)((origin.Y + (double)point.Y * size.Height) * scale));
    }

    // Subset to the semantic source footprint when the backing buffer includes an apron so Clamp never bleeds apron pixels.
    private static SKShader? CreateSourceShader(
        SKImage? image,
        GRRecordingContext? recordingContext,
        Rect sourceBounds,
        RenderPlacement src,
        SKSamplingOptions sampling,
        out Rect rasterBounds)
    {
        rasterBounds = src.RasterBounds;
        if (image is null) return null;

        var imageBounds = new PixelRect(new PixelSize(image.Width, image.Height));
        (_, PixelRect semanticSubset, Rect semanticRasterBounds) = RasterShaderMapping.ResolveSemanticSubset(
            sourceBounds, src.Scale.Value, src.DeviceBounds, src.RasterBounds);

        if (semanticSubset != imageBounds
            && semanticSubset.Width > 0 && semanticSubset.Height > 0
            && imageBounds.Contains(semanticSubset))
        {
            using SKImage? subset = RasterShaderMapping.SubsetImage(image, recordingContext, semanticSubset);
            if (subset is not null)
            {
                rasterBounds = semanticRasterBounds;
                return subset.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
            }
        }

        return image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
    }
}
