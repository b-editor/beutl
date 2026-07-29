using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class FormattedTextRasterBoundsTests
{
    private const int Origin = 200;
    private const int SurfaceWidth = 700;
    private const int SurfaceHeight = 500;

    private static IEnumerable<TestCaseData> Cases()
    {
        foreach (string content in new[] { "Hjgl 日本語", "Beutl", "ABCdefg", "がぎぐげご", "|_^~gjpqy" })
        {
            foreach (int size in new[] { 9, 11, 16, 23, 32, 48, 67, 89 })
            {
                yield return new TestCaseData(content, size);
            }
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void ActualBounds_ContainsEveryRasterizedGlyphPixel(string content, int size)
    {
        using FormattedText text = CreateText(content, size);
        Rect declared = text.ActualBounds;

        PixelRect raster = RasterizeMask(text);

        Assert.Multiple(() =>
        {
            Assert.That(declared.X, Is.LessThanOrEqualTo(raster.X), "left");
            Assert.That(declared.Y, Is.LessThanOrEqualTo(raster.Y), "top");
            Assert.That(declared.Right, Is.GreaterThanOrEqualTo(raster.Right), "right");
            Assert.That(declared.Bottom, Is.GreaterThanOrEqualTo(raster.Bottom), "bottom");
        });
    }

    [TestCaseSource(nameof(Cases))]
    public void ActualBounds_WithPen_ContainsEveryRasterizedGlyphPixel(string content, int size)
    {
        using FormattedText text = CreateText(content, size);
        text.Pen = new Pen
        {
            Thickness = { CurrentValue = 3f },
            Brush = { CurrentValue = Brushes.White }
        }.ToResource(CompositionContext.Default);
        Rect declared = text.ActualBounds;

        PixelRect raster = RasterizeMask(text);

        Assert.Multiple(() =>
        {
            Assert.That(declared.X, Is.LessThanOrEqualTo(raster.X), "left");
            Assert.That(declared.Y, Is.LessThanOrEqualTo(raster.Y), "top");
            Assert.That(declared.Right, Is.GreaterThanOrEqualTo(raster.Right), "right");
            Assert.That(declared.Bottom, Is.GreaterThanOrEqualTo(raster.Bottom), "bottom");
        });
    }

    private static FormattedText CreateText(string content, int size)
    {
        Typeface typeface = TypefaceProvider.Typeface();
        return new FormattedText
        {
            Font = typeface.FontFamily,
            Style = typeface.Style,
            Weight = typeface.Weight,
            Size = size,
            Text = new StringSpan(content, 0, content.Length),
        };
    }

    /// <summary>Extent of the covered pixels, in the text's own coordinates.</summary>
    private static PixelRect RasterizeMask(FormattedText text)
    {
        SKTextBlob blob = text.GetTextBlob() ?? throw new InvalidOperationException("The text shaped to no glyphs.");
        SKPath? stroke = text.GetStrokePath();

        var info = new SKImageInfo(SurfaceWidth, SurfaceHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(Origin, Origin);
            canvas.DrawText(blob, 0, 0, paint);
            if (stroke is not null)
                canvas.DrawPath(stroke, paint);
        }

        int top = -1;
        int bottom = -1;
        int left = int.MaxValue;
        int right = -1;
        for (int y = 0; y < info.Height; y++)
        {
            for (int x = 0; x < info.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0)
                    continue;

                if (top < 0)
                    top = y;
                bottom = y;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
            }
        }

        if (top < 0)
            throw new InvalidOperationException("The text rasterized to nothing.");

        Assert.That(
            new[] { left, top },
            Is.All.GreaterThan(0),
            "the probe surface must not clip the mask");
        Assert.Multiple(() =>
        {
            Assert.That(right, Is.LessThan(info.Width - 1), "the probe surface must not clip the mask");
            Assert.That(bottom, Is.LessThan(info.Height - 1), "the probe surface must not clip the mask");
        });

        return new PixelRect(left - Origin, top - Origin, right + 1 - left, bottom + 1 - top);
    }
}
