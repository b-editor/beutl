using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal sealed class PuppetMeshCache
{
    private const int AnalysisLongestSide = 1024;
    private static readonly byte[] s_alphaThreshold = Enumerable.Range(0, 256)
        .Select(alpha => alpha < AlphaOutlineTracer.BackgroundAlpha ? (byte)0 : (byte)255).ToArray();
    private Size _size;
    private PixelSize _maskSize;
    private byte[]? _coverage;
    private Point[]? _anchors;
    private (PuppetMesh Mesh, PuppetDeformer Deformer)? _prepared;

    public (PuppetMesh Mesh, PuppetDeformer Deformer)? GetOrCreate(EffectTarget source, Point[] anchors)
    {
        RenderTarget input = source.RenderTarget!;
        GRRecordingContext? graphicsContext = input.RawValue.Context;
        input.PrepareForSampling(RenderTargetSamplingIntent.SameContextTextureSampling(graphicsContext));
        using SurfaceSnapshot.Lease snapshot = SurfaceSnapshot.Take(input.Value);
        Size size = source.Bounds.Size;
        using Bitmap mask = SampleMask(snapshot.Image, source, graphicsContext, AnalysisLongestSide);
        var maskSize = new PixelSize(mask.Width, mask.Height);
        if (_size == size && _maskSize == maskSize && anchors.AsSpan().SequenceEqual(_anchors)
            && _coverage is { } cachedCoverage && HasCoverage(mask, cachedCoverage))
            return _prepared;

        _size = size;
        _maskSize = maskSize;
        _anchors = anchors;
        _coverage = new byte[mask.Width * mask.Height];
        for (int y = 0; y < mask.Height; y++)
            mask.GetRow(y).CopyTo(_coverage.AsSpan(y * mask.Width));

        _prepared = null;
        IReadOnlyList<Point[]> outlines = AlphaOutlineTracer.Trace(mask);
        int longestSide = AnalysisLongestSide;
        while (true)
        {
            if (outlines.Sum(outline => outline.Length) <= PuppetMesh.BoundaryVertexBudget)
            {
                PuppetMesh? mesh = PuppetMesh.Create(outlines, size, maskSize, anchors, out bool exceedsVertexBudget);
                if (!exceedsVertexBudget)
                {
                    if (mesh is not null)
                        _prepared = (mesh, new PuppetDeformer(mesh));
                    return _prepared;
                }
            }

            if (longestSide == 1) return null;

            longestSide /= 2;
            using Bitmap reduced = SampleMask(snapshot.Image, source, graphicsContext, longestSide);
            outlines = AlphaOutlineTracer.Trace(reduced);
            maskSize = new PixelSize(reduced.Width, reduced.Height);
        }
    }

    private static bool HasCoverage(Bitmap mask, byte[] pixels)
    {
        for (int y = 0; y < mask.Height; y++)
        {
            if (!mask.GetRow(y).SequenceEqual(pixels.AsSpan(y * mask.Width, mask.Width)))
                return false;
        }
        return true;
    }

    // Trace the support of the alpha on a logical grid. The original texture supplies the translucent edge pixels.
    // Sample and threshold on the source's GPU before reading back only the small Alpha8 mask.
    private static Bitmap SampleMask(
        SKImage image, EffectTarget source, GRRecordingContext? graphicsContext, int longestSide)
    {
        Rect bounds = source.Bounds;
        RenderPlacement placement = source.Placement;
        double density = Math.Min(1, longestSide / (double)Math.Max(bounds.Width, bounds.Height));
        int width = Math.Max(1, (int)Math.Ceiling(bounds.Width * density));
        int height = Math.Max(1, (int)Math.Ceiling(bounds.Height * density));
        double sx = width / (double)bounds.Width;
        double sy = height / (double)bounds.Height;
        float scale = placement.Scale.Value;
        SKMatrix imageToMask = SKMatrix.CreateScaleTranslation(
            (float)(sx / scale), (float)(sy / scale),
            (float)(((double)placement.RasterBounds.X - bounds.X) * sx),
            (float)(((double)placement.RasterBounds.Y - bounds.Y) * sy));
        using var threshold = SKColorFilter.CreateTable(s_alphaThreshold);
        var mask = new Bitmap(width, height, BitmapColorType.Alpha8, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
        try
        {
            if (graphicsContext is null)
            {
                using var canvas = new SKCanvas(mask.SKBitmap);
                DrawMask(canvas, image);
                return mask;
            }

            SKImageInfo maskInfo = mask.SKBitmap.Info;
            using (SKSurface? gpuMask = SKSurface.Create(graphicsContext, true, maskInfo)
                                        ?? SKSurface.Create(graphicsContext, true, maskInfo.WithColorType(SKColorType.Rgba8888)))
            {
                if (gpuMask is not null)
                {
                    DrawMask(gpuMask.Canvas, image);
                    if (gpuMask.ReadPixels(maskInfo, mask.Data, mask.RowBytes, 0, 0))
                        return mask;
                }
            }

            // If the analysis surface cannot be rendered or read, sample a raster alpha snapshot instead.
            using Bitmap alpha = source.RenderTarget!.SnapshotAlpha();
            alpha.SKBitmap.SetImmutable();
            using SKImage rasterImage = SKImage.FromBitmap(alpha.SKBitmap);
            using var rasterCanvas = new SKCanvas(mask.SKBitmap);
            DrawMask(rasterCanvas, rasterImage);
            return mask;
        }
        catch
        {
            mask.Dispose();
            throw;
        }

        void DrawMask(SKCanvas canvas, SKImage sourceImage)
        {
            using SKShader shader = sourceImage.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
                new SKSamplingOptions(SKFilterMode.Linear), imageToMask);
            using var paint = new SKPaint { Shader = shader, ColorFilter = threshold, BlendMode = SKBlendMode.Src };
            canvas.DrawPaint(paint);
        }
    }
}
