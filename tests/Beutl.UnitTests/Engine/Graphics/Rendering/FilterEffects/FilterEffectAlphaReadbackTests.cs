using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.FilterEffects;

[TestFixture]
[NonParallelizable]
public sealed class FilterEffectAlphaReadbackTests
{
    private static readonly PixelSize s_sourceSize = new(72, 56);

    [Test]
    [Category("GpuPassFusionGpu")]
    public void SnapshotAlpha_ReadsAlpha8AndMatchesEffectItemConversionExactly()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget target = CreateAlphaRampTarget();
            using Bitmap fullColor = target.Snapshot();
            using Bitmap expected = fullColor.Convert(BitmapColorType.Alpha8);
            using Bitmap actual = target.SnapshotAlpha();

            Assert.Multiple(() =>
            {
                Assert.That(actual.ColorType, Is.EqualTo(BitmapColorType.Alpha8));
                Assert.That(actual.AlphaType, Is.EqualTo(BitmapAlphaType.Premul));
                Assert.That(actual.ColorSpace.Equals(BitmapColorSpace.LinearSrgb), Is.True);
                Assert.That(actual.BytesPerPixel, Is.EqualTo(1));
                Assert.That(actual.RowBytes, Is.EqualTo(actual.Width));
                Assert.That(actual.ByteCount, Is.EqualTo(actual.Width * actual.Height));
            });
            AssertRowsIdentical(expected, actual, "effectItem RgbaF16-to-Alpha8 conversion", "direct Alpha8 readback");
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void SnapshotAlpha_RetainsCompletionWaitForCpuReadback()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            GpuResourceReclaimQueue.FlushAndDrain();
            using RenderTarget target = CreatePatternTarget();
            var flushes = new List<ImmediateCanvasFlushKind>();

            using (ImmediateCanvas.ObserveFlushes(flushes.Add))
            using (target.SnapshotAlpha())
            {
            }

            Assert.That(
                flushes,
                Is.EqualTo(new[] { ImmediateCanvasFlushKind.PrepareForSampling }),
                "Alpha readback must wait for completion rather than using submit-only sampling.");
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void ClippingAutoClip_DirectAlphaReadbackMatchesEffectItemBoundsExactly()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = CreatePatternTarget();
            using Bitmap expectedAlpha = SnapshotClippingAlphaEffectItem(source);
            using Bitmap actualAlpha = source.SnapshotAlpha();

            Thickness expected = FindAutoClipThickness(expectedAlpha);
            Thickness actual = FindAutoClipThickness(actualAlpha);

            Assert.That(actual, Is.EqualTo(expected));
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void FlatShadow_DirectAlphaReadbackMatchesEffectItemContourRenderingExactly()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = CreatePatternTarget();
            using Bitmap effectItemSource = source.Snapshot();
            using Bitmap directAlpha = source.SnapshotAlpha();
            using Bitmap expected = RenderFlatShadowContours(effectItemSource);
            using Bitmap actual = RenderFlatShadowContours(directAlpha);

            AssertRowsIdentical(expected, actual, "effectItem FlatShadow contours", "direct-alpha FlatShadow contours");
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void StrokeEffect_DirectAlphaReadbackMatchesEffectItemContourRenderingExactly()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = CreatePatternTarget();
            using Bitmap effectItemSource = source.Snapshot();
            using Bitmap directAlpha = source.SnapshotAlpha();
            using Bitmap expected = RenderStrokeContours(effectItemSource);
            using Bitmap actual = RenderStrokeContours(directAlpha);

            AssertRowsIdentical(expected, actual, "effectItem StrokeEffect contours", "direct-alpha StrokeEffect contours");
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void PartsSplitEffect_DirectAlphaReadbackMatchesEffectItemContourRenderingExactly()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = CreatePatternTarget();
            using Bitmap effectItemSource = source.Snapshot();
            using Bitmap directAlpha = source.SnapshotAlpha();
            using Bitmap expected = RenderSplitContours(effectItemSource);
            using Bitmap actual = RenderSplitContours(directAlpha);

            AssertRowsIdentical(expected, actual, "effectItem PartsSplitEffect contours", "direct-alpha PartsSplitEffect contours");
        });
    }

    private static Bitmap SnapshotClippingAlphaEffectItem(RenderTarget target)
    {
        target.Value.Flush(true, true);
        using SKImage image = target.Value.Snapshot();
        return image.ToBitmap(BitmapColorType.Alpha8);
    }

    private static Thickness FindAutoClipThickness(Bitmap bitmap)
    {
        int x0 = bitmap.Width;
        int y0 = bitmap.Height;
        int x1 = 0;
        int y1 = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (row[x] == 0)
                    continue;

                if (x0 > x) x0 = x;
                if (y0 > y) y0 = y;
                if (x1 < x) x1 = x;
                if (y1 < y) y1 = y;
            }
        }

        return new Thickness(x0, y0, bitmap.Width - x1, bitmap.Height - y1);
    }

    private static Bitmap RenderFlatShadowContours(Bitmap source)
    {
        using SKPath path = CreateContourPath(source);
        Bitmap result = CreateReferenceBitmap();
        using var canvas = new SKCanvas(result.SKBitmap);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        float x = MathF.Cos(MathF.PI * 31 / 180);
        float y = MathF.Sin(MathF.PI * 31 / 180);
        for (int i = 0; i < 13; i++)
        {
            canvas.Translate(x, y);
            canvas.DrawPath(path, paint);
        }

        return result;
    }

    private static Bitmap RenderStrokeContours(Bitmap source)
    {
        using SKPath path = CreateContourPath(source);
        Bitmap result = CreateReferenceBitmap();
        using var canvas = new SKCanvas(result.SKBitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(4, 3);
        using var paint = new SKPaint
        {
            Color = SKColors.Blue,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 7,
        };
        canvas.DrawPath(path, paint);
        return result;
    }

    private static Bitmap RenderSplitContours(Bitmap source)
    {
        List<SKPath> paths = CreateSplitPaths(source);
        try
        {
            Bitmap result = CreateReferenceBitmap();
            using var canvas = new SKCanvas(result.SKBitmap);
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            SKColor[] colors = [SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Yellow];
            for (int i = 0; i < paths.Count; i++)
            {
                paint.Color = colors[i % colors.Length];
                canvas.DrawPath(paths[i], paint);
            }

            return result;
        }
        finally
        {
            foreach (SKPath path in paths)
                path.Dispose();
        }
    }

    private static SKPath CreateContourPath(Bitmap source)
    {
        using Contours contours = ContourTracer.FindContours(source);
        var path = new SKPath();
        for (int contourIndex = 0; contourIndex < contours.Count; contourIndex++)
        {
            ReadOnlySpan<PixelPoint> contour = contours[contourIndex];
            for (int i = 0; i < contour.Length; i++)
            {
                if (i == 0)
                    path.MoveTo(contour[i].X, contour[i].Y);
                else
                    path.LineTo(contour[i].X, contour[i].Y);
            }
            path.Close();
        }

        return path;
    }

    private static List<SKPath> CreateSplitPaths(Bitmap source)
    {
        ContourTracer.FindContoursWithHierarchy(source, out Contours contours, out var parentIndices);
        using (contours)
        using (parentIndices)
        {
            var paths = new List<(SKPath Path, int Parent, int Index)>(contours.Count);
            for (int i = 0; i < contours.Count; i++)
            {
                ReadOnlySpan<PixelPoint> contour = contours[i];
                var path = new SKPath();
                for (int j = 0; j < contour.Length; j++)
                {
                    if (j == 0)
                        path.MoveTo(contour[j].X, contour[j].Y);
                    else
                        path.LineTo(contour[j].X, contour[j].Y);
                }
                path.Close();
                paths.Add((path, parentIndices[i], i));
            }

            for (int i = 0; i < paths.Count; i++)
            {
                (SKPath path, int parent, int _) = paths[i];
                if (parent < 0)
                    continue;

                int parentIndex = paths.FindIndex(item => item.Index == parent);
                if (parentIndex < 0)
                    continue;

                (SKPath parentPath, int grandParent, int originalIndex) = paths[parentIndex];
                SKPath? merged = parentPath.Op(path, SKPathOp.Xor);
                if (merged is null)
                    continue;

                path.Dispose();
                parentPath.Dispose();
                paths[parentIndex] = (merged, grandParent, originalIndex);
                paths.RemoveAt(i);
                if (parentIndex < i)
                    i--;
            }

            return paths.Select(static item => item.Path).ToList();
        }
    }

    private static Bitmap CreateReferenceBitmap()
        => new(
            96,
            80,
            BitmapColorType.RgbaF16,
            BitmapAlphaType.Premul,
            BitmapColorSpace.LinearSrgb);

    private static RenderTarget CreatePatternTarget()
    {
        RenderTarget target = RenderTarget.Create(s_sourceSize.Width, s_sourceSize.Height)
            ?? throw new InvalidOperationException("Could not create the GPU filter-effect source target.");
        SKCanvas canvas = target.Value.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            BlendMode = SKBlendMode.Src,
        };
        canvas.DrawRoundRect(new SKRect(5.25f, 4.5f, 41.75f, 46.25f), 5, 5, paint);
        canvas.DrawOval(new SKRect(48.5f, 12.25f, 67.25f, 35.75f), paint);

        paint.BlendMode = SKBlendMode.Clear;
        canvas.DrawOval(new SKRect(16.5f, 16.25f, 30.75f, 33.5f), paint);
        return target;
    }

    private static RenderTarget CreateAlphaRampTarget()
    {
        const int width = 256;
        const int height = 8;
        RenderTarget target = RenderTarget.Create(width, height)
            ?? throw new InvalidOperationException("Could not create the GPU alpha-ramp target.");
        SKCanvas canvas = target.Value.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint
        {
            IsAntialias = false,
            BlendMode = SKBlendMode.Src,
        };
        for (int x = 0; x < width; x++)
        {
            paint.Color = new SKColor(255, 255, 255, (byte)x);
            canvas.DrawRect(x, 0, 1, 4, paint);
        }

        paint.Color = SKColors.White;
        paint.IsAntialias = true;
        canvas.DrawLine(0.25f, 7.25f, 255.75f, 4.25f, paint);
        return target;
    }

    private static void AssertRowsIdentical(Bitmap expected, Bitmap actual, string expectedPath, string actualPath)
    {
        Assert.That(actual.Width, Is.EqualTo(expected.Width));
        Assert.That(actual.Height, Is.EqualTo(expected.Height));
        for (int y = 0; y < expected.Height; y++)
        {
            Assert.That(
                actual.GetRow(y).SequenceEqual(expected.GetRow(y)),
                Is.True,
                $"row {y} differs between {expectedPath} and {actualPath}");
        }
    }
}
