using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal sealed class PuppetMeshCache
{
    private const int AnalysisLongestSide = 1024;
    private const int SamplesPerBatch = 8;
    private static readonly Lazy<SKRuntimeEffect> s_coverageEffect = new(CreateCoverageEffect);
    private Size _size;
    private PixelSize _maskSize;
    private byte[]? _coverage;
    private Point[]? _anchors;
    private (PuppetMesh Mesh, PuppetDeformer Deformer)? _prepared;

    public (PuppetMesh Mesh, PuppetDeformer Deformer)? GetOrCreate(EffectTarget source, Point[] anchors)
    {
        Size size = source.Bounds.Size;
        float scale = source.Placement.Scale.Value;
        PixelSize maskSize = CalculateMaskSize(size, scale, AnalysisLongestSide);
        using Bitmap mask = SampleMask(source, maskSize);
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
            using Bitmap reduced = ReduceMask(mask, CalculateMaskSize(size, scale, longestSide));
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

    // Reduce coverage on the GPU before reading back. Raster targets can scan their Alpha8 pixels directly.
    private static Bitmap SampleMask(EffectTarget source, PixelSize size)
    {
        RenderTarget input = source.RenderTarget!;
        Rect bounds = source.Bounds;
        RenderPlacement placement = source.Placement;
        float scale = placement.Scale.Value;
        var origin = new SKPoint((bounds.X - placement.RasterBounds.X) * scale,
            (bounds.Y - placement.RasterBounds.Y) * scale);
        var footprint = new SKSize(bounds.Width * scale / size.Width, bounds.Height * scale / size.Height);
        var mask = CreateMask(size);
        try
        {
            if (input.RawValue.Context is { } graphicsContext)
            {
                input.PrepareForSampling(RenderTargetSamplingIntent.SameContextTextureSampling(graphicsContext));
                using SurfaceSnapshot.Lease snapshot = SurfaceSnapshot.Take(input.Value);
                SKImageInfo maskInfo = mask.SKBitmap.Info;
                using SKSurface? gpuMask = SKSurface.Create(graphicsContext, true, maskInfo)
                    ?? SKSurface.Create(graphicsContext, true, maskInfo.WithColorType(SKColorType.Rgba8888));
                if (gpuMask is not null)
                {
                    DrawCoverage(gpuMask.Canvas, snapshot.Image, origin, footprint);
                    if (gpuMask.ReadPixels(maskInfo, mask.Data, mask.RowBytes, 0, 0))
                        return mask;
                }
            }

            using Bitmap alpha = input.SnapshotAlpha();
            CopyCoverage(alpha, mask, origin, footprint);
            return mask;
        }
        catch
        {
            mask.Dispose();
            throw;
        }
    }

    private static PixelSize CalculateMaskSize(Size size, float scale, int longestSide)
    {
        // Keep the extra edge precision of a supersampled input when it fits in the analysis grid.
        double density = Math.Min(Math.Max(1, scale), longestSide / (double)Math.Max(size.Width, size.Height));
        return new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * density)),
            Math.Max(1, (int)Math.Ceiling(size.Height * density)));
    }

    private static Bitmap CreateMask(PixelSize size)
        => new(size.Width, size.Height, BitmapColorType.Alpha8, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);

    private static Bitmap ReduceMask(Bitmap mask, PixelSize size)
    {
        var reduced = CreateMask(size);
        CopyCoverage(mask, reduced, default, new SKSize(mask.Width / (float)size.Width, mask.Height / (float)size.Height));
        return reduced;
    }

    private static void CopyCoverage(Bitmap source, Bitmap mask, SKPoint origin, SKSize footprint)
    {
        mask.GetPixelSpan().Clear();
        const byte backgroundMax = AlphaOutlineTracer.BackgroundAlpha - 1;
        for (int y = 0; y < source.Height; y++)
        {
            var (top, bottom) = MaskCells(y, y + 1, origin.Y, footprint.Height, source.Height, mask.Height);
            if (top == bottom) continue;

            Span<byte> pixels = source.GetRow(y);
            int x = 0;
            while (x < pixels.Length)
            {
                int offset = pixels[x..].IndexOfAnyExceptInRange((byte)0, backgroundMax);
                if (offset < 0) break;

                int start = x + offset;
                int length = pixels[start..].IndexOfAnyInRange((byte)0, backgroundMax);
                int end = length < 0 ? pixels.Length : start + length;
                var (left, right) = MaskCells(start, end, origin.X, footprint.Width, source.Width, mask.Width);
                for (int row = top; row < bottom; row++)
                    mask.GetRow(row)[left..right].Fill(255);
                x = end;
            }
        }

        // Project each covered run onto the overlapping mask cells. The edge pixels clamp as on the GPU.
        static (int First, int End) MaskCells(int start, int end, float origin, float footprint, int sourceLength, int maskLength)
        {
            int first = start == 0 ? 0 : Math.Clamp((int)MathF.Floor((start - origin) / footprint), 0, maskLength);
            int last = end == sourceLength ? maskLength : Math.Clamp((int)MathF.Ceiling((end - origin) / footprint), 0, maskLength);
            return (first, last);
        }
    }

    private static void DrawCoverage(SKCanvas canvas, SKImage image, SKPoint origin, SKSize footprint)
    {
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
                // A partially covered source pixel can contribute to both of its neighbors in the mask.
                float2 first = floor(origin + cell * footprint);
                float2 end = ceil(origin + (cell + 1.0) * footprint);
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
