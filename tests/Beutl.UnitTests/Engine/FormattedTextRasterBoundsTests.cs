using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class FormattedTextRasterBoundsTests
{
    private static FormattedText CreateText(string text, float size)
        => new()
        {
            Text = new StringSpan(text, 0, text.Length),
            Font = FontFamily.Default,
            Size = size,
        };

    // Full hinting can move a glyph mask off its unhinted outline, so a renderer that allocates
    // ActualBounds clips the row the mask spills into.
    [TestCase(24f)]
    [TestCase(48f)]
    [TestCase(96f)]
    public void RasterBounds_ContainsEveryRasterizedGlyphPixel(float size)
    {
        using FormattedText text = CreateText("Your model", size);
        Rect raster = text.RasterBounds;
        Assert.That(raster.IsEmpty, Is.False);

        var device = PixelRect.FromRect(raster, 1);
        using var surface = SKSurface.Create(
            new SKImageInfo(device.Width, device.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText(text.GetTextBlob(), -device.X, -device.Y, paint);
        }

        canvas.Flush();
        using SKImage image = surface.Snapshot();
        using SKBitmap bitmap = SKBitmap.FromImage(image);

        int touchedTop = -1;
        int touchedBottom = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0)
                    continue;
                touchedTop = touchedTop < 0 ? y : touchedTop;
                touchedBottom = y;
                break;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(touchedTop, Is.GreaterThanOrEqualTo(0),
                "the fixture must actually rasterize glyphs");
            Assert.That(touchedTop, Is.GreaterThan(0),
                "a mask touching row 0 means RasterBounds did not leave room above the glyphs");
            Assert.That(touchedBottom, Is.LessThan(bitmap.Height - 1),
                "a mask touching the last row means RasterBounds did not leave room below the glyphs");
        });
    }

    // Only the allocated footprint may widen: brush mapping and layout read the semantic bounds, and
    // moving them shifts gradients and alignment.
    [Test]
    public void RasterBounds_ContainsActualBounds_WithoutChangingIt()
    {
        using FormattedText text = CreateText("Your model", 48f);
        Rect actual = text.ActualBounds;
        Rect raster = text.RasterBounds;

        Assert.Multiple(() =>
        {
            Assert.That(raster.X, Is.LessThanOrEqualTo(actual.X));
            Assert.That(raster.Y, Is.LessThanOrEqualTo(actual.Y));
            Assert.That(raster.Right, Is.GreaterThanOrEqualTo(actual.Right));
            Assert.That(raster.Bottom, Is.GreaterThanOrEqualTo(actual.Bottom));
        });
    }

    // The current SkiaSharp runtime leaves SKTextBlobBuilder run storage readable after Build(), so the
    // lifetime hazard this guards is not observably red before the production reorder. Repeated measurement
    // still verifies that moving mask-bound calculation before Build() preserves the published footprint.
    [Test]
    public void RasterBounds_RemainsStableAcrossRepeatedMeasurement()
    {
        using FormattedText text = CreateText("Builder span lifetime", 48f);
        Rect expected = text.RasterBounds;

        for (int i = 0; i < 8; i++)
        {
            text.Size = 49f;
            _ = text.RasterBounds;
            text.Size = 48f;

            Assert.That(text.RasterBounds, Is.EqualTo(expected), $"RasterBounds changed after measurement cycle {i + 1}.");
        }
    }

    [Test]
    public void AddToSKPath_RemainsStableWhenRunStorageIsConsumedBeforeBuild()
    {
        using FormattedText text = CreateText("Outline", 48f);
        using var first = new SKPath();
        using var second = new SKPath();

        text.AddToSKPath(first, new Point(10, 20));
        text.AddToSKPath(second, new Point(10, 20));

        Assert.That(second.TightBounds, Is.EqualTo(first.TightBounds));
    }

}
