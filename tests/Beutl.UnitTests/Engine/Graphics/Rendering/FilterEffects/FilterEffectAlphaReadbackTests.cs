using Beutl.Collections.Pooled;
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

    // FlatShadow and StrokeEffect outline these contours, and PartsSplitEffect splits by the hierarchy, so
    // identical contours from both readbacks give identical effect output.
    [Test]
    public void FindContours_DirectAlphaReadbackMatchesEffectItemContoursExactly()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = CreatePatternTarget();
            using Bitmap effectItemSource = source.Snapshot();
            using Bitmap directAlpha = source.SnapshotAlpha();
            using Contours expected = ContourTracer.FindContours(effectItemSource);
            using Contours actual = ContourTracer.FindContours(directAlpha);

            Assert.That(Points(actual), Is.EqualTo(Points(expected)));
        });
    }

    [Test]
    public void FindContoursWithHierarchy_DirectAlphaReadbackMatchesEffectItemContoursExactly()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = CreatePatternTarget();
            using Bitmap effectItemSource = source.Snapshot();
            using Bitmap directAlpha = source.SnapshotAlpha();
            ContourTracer.FindContoursWithHierarchy(
                effectItemSource,
                out Contours expected,
                out PooledList<int> expectedParents);
            ContourTracer.FindContoursWithHierarchy(directAlpha, out Contours actual, out PooledList<int> actualParents);
            using (expected)
            using (expectedParents)
            using (actual)
            using (actualParents)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(Points(actual), Is.EqualTo(Points(expected)));
                    Assert.That(actualParents.ToArray(), Is.EqualTo(expectedParents.ToArray()));
                });
            }
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

    private static PixelPoint[][] Points(Contours contours)
        => [.. Enumerable.Range(0, contours.Count).Select(index => contours[index].ToArray())];

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
