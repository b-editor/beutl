using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal sealed class PuppetMeshCache
{
    private const int AnalysisLongestSide = 1024;
    private const int SamplesPerBatch = 8;
    private static readonly byte[] s_alphaThreshold = Enumerable.Range(0, 256)
        .Select(alpha => alpha < AlphaOutlineTracer.BackgroundAlpha ? (byte)0 : (byte)255).ToArray();
    private static readonly Lazy<SKRuntimeEffect> s_coverageEffect = new(CreateCoverageEffect);
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
            // Every retry is a reduction of the same coverage used as the cache key.
            using Bitmap reduced = ReduceMask(mask, CalculateMaskSize(size, longestSide));
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

    // Read back only the small Alpha8 mask. For wider footprints a single bilinear sample can miss a source pixel.
    private static Bitmap SampleMask(
        SKImage image, EffectTarget source, GRRecordingContext? graphicsContext, int longestSide)
    {
        Rect bounds = source.Bounds;
        RenderPlacement placement = source.Placement;
        PixelSize size = CalculateMaskSize(bounds.Size, longestSide);
        float scale = placement.Scale.Value;
        var origin = new SKPoint((bounds.X - placement.RasterBounds.X) * scale,
            (bounds.Y - placement.RasterBounds.Y) * scale);
        var footprint = new SKSize(bounds.Width * scale / size.Width, bounds.Height * scale / size.Height);
        var mask = CreateMask(size);
        try
        {
            if (graphicsContext is null)
            {
                using var canvas = new SKCanvas(mask.SKBitmap);
                DrawCoverage(canvas, image, origin, footprint);
                return mask;
            }

            SKImageInfo maskInfo = mask.SKBitmap.Info;
            using (SKSurface? gpuMask = SKSurface.Create(graphicsContext, true, maskInfo)
                                        ?? SKSurface.Create(graphicsContext, true, maskInfo.WithColorType(SKColorType.Rgba8888)))
            {
                if (gpuMask is not null)
                {
                    DrawCoverage(gpuMask.Canvas, image, origin, footprint);
                    if (gpuMask.ReadPixels(maskInfo, mask.Data, mask.RowBytes, 0, 0))
                        return mask;
                }
            }

            // If the analysis surface cannot be rendered or read, sample a raster alpha snapshot instead.
            using Bitmap alpha = source.RenderTarget!.SnapshotAlpha();
            alpha.SKBitmap.SetImmutable();
            using SKImage rasterImage = SKImage.FromBitmap(alpha.SKBitmap);
            using var rasterCanvas = new SKCanvas(mask.SKBitmap);
            DrawCoverage(rasterCanvas, rasterImage, origin, footprint);
            return mask;
        }
        catch
        {
            mask.Dispose();
            throw;
        }
    }

    private static PixelSize CalculateMaskSize(Size size, int longestSide)
    {
        double density = Math.Min(1, longestSide / (double)Math.Max(size.Width, size.Height));
        return new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * density)),
            Math.Max(1, (int)Math.Ceiling(size.Height * density)));
    }

    private static Bitmap CreateMask(PixelSize size)
        => new(size.Width, size.Height, BitmapColorType.Alpha8, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);

    private static Bitmap ReduceMask(Bitmap mask, PixelSize size)
    {
        var reduced = CreateMask(size);
        reduced.GetPixelSpan().Clear();
        for (int y = 0; y < mask.Height; y++)
        {
            Span<byte> source = mask.GetRow(y);
            Span<byte> destination = reduced.GetRow((2 * y + 1) * size.Height / (2 * mask.Height));
            for (int x = 0; x < source.Length; x++)
            {
                if (source[x] != 0)
                    destination[(2 * x + 1) * size.Width / (2 * mask.Width)] = 255;
            }
        }
        return reduced;
    }

    private static void DrawCoverage(SKCanvas canvas, SKImage image, SKPoint origin, SKSize footprint)
    {
        if (footprint is { Width: <= 2, Height: <= 2 })
        {
            var transform = SKMatrix.CreateScaleTranslation(1 / footprint.Width, 1 / footprint.Height,
                -origin.X / footprint.Width, -origin.Y / footprint.Height);
            using SKShader shader = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
                new SKSamplingOptions(SKFilterMode.Linear), transform);
            using var threshold = SKColorFilter.CreateTable(s_alphaThreshold);
            using var linear = new SKPaint { Shader = shader, ColorFilter = threshold, BlendMode = SKBlendMode.Src };
            canvas.DrawPaint(linear);
            return;
        }

        SKRuntimeEffect effect = s_coverageEffect.Value;
        using SKShader source = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
            new SKSamplingOptions(SKFilterMode.Nearest));
        using var uniforms = new SKRuntimeEffectUniforms(effect);
        using var children = new SKRuntimeEffectChildren(effect);
        children["src"] = source;
        uniforms["origin"] = new[] { origin.X, origin.Y };
        uniforms["footprint"] = new[] { footprint.Width, footprint.Height };
        using var paint = new SKPaint();
        canvas.Clear(SKColors.Transparent);
        int columns = (int)Math.Ceiling(footprint.Width) + 1;
        int rows = (int)Math.Ceiling(footprint.Height) + 1;
        for (int y = 0; y < rows; y += SamplesPerBatch)
        {
            for (int x = 0; x < columns; x += SamplesPerBatch)
            {
                uniforms["offset"] = new[] { (float)x, (float)y };
                using SKShader coverage = effect.ToShader(uniforms, children);
                paint.Shader = coverage;
                canvas.DrawPaint(paint);
            }
        }
    }

    private static SKRuntimeEffect CreateCoverageEffect()
        => SKRuntimeEffect.CreateShader($$"""
            uniform shader src;
            uniform float2 origin;
            uniform float2 footprint;
            uniform float2 offset;

            half4 main(float2 p)
            {
                float2 cell = floor(p);
                float2 first = ceil(origin + cell * footprint - 0.5);
                float2 end = ceil(origin + (cell + 1.0) * footprint - 0.5);
                // A cell that is smaller than a raster pixel can contain no pixel center.
                // In that dimension sample the pixel containing the center of the cell.
                float2 centerPixel = floor(origin + (cell + 0.5) * footprint);
                if (first.x >= end.x) { first.x = centerPixel.x; end.x = first.x + 1.0; }
                if (first.y >= end.y) { first.y = centerPixel.y; end.y = first.y + 1.0; }
                first += offset;
                for (int row = 0; row < {{SamplesPerBatch}}; row++)
                {
                    float y = first.y + float(row);
                    if (y >= end.y) break;
                    for (int col = 0; col < {{SamplesPerBatch}}; col++)
                    {
                        float x = first.x + float(col);
                        if (x >= end.x) break;
                        if (src.eval(float2(x, y) + 0.5).a >= {{AlphaOutlineTracer.BackgroundAlpha}}.0 / 255.0)
                            return half4(0, 0, 0, 1);
                    }
                }
                return half4(0);
            }
            """, out string? error)
            ?? throw new InvalidOperationException($"Could not compile the Puppet alpha coverage shader: {error}");
}
