using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class FormattedTextRasterBoundsTests
{
    private static readonly float[] s_sizes = [12f, 16f, 20f, 24f, 32f, 40f, 48f, 64f, 96f, 144f];

    private static IEnumerable<TestCaseData> RasterBoundsCases()
    {
        foreach (float size in s_sizes)
        {
            yield return new TestCaseData(size, false)
                .SetName($"RasterBounds_FillOnly_{size:g}_ContainsMaskWithFourSideHeadroom");
            yield return new TestCaseData(size, true)
                .SetName($"RasterBounds_ThickStroke_{size:g}_ContainsMaskWithFourSideHeadroom");
        }
    }

    private static FormattedText CreateText(string text, float size, Pen.Resource? pen = null)
        => new()
        {
            Text = new StringSpan(text, 0, text.Length),
            Font = FontFamily.Default,
            Size = size,
            Pen = pen,
        };

    [TestCaseSource(nameof(RasterBoundsCases))]
    public void RasterBounds_ContainsEveryRasterizedGlyphPixelWithHeadroom(
        float size,
        bool useThickStroke)
    {
        using Pen.Resource? pen = useThickStroke ? CreateThickPen(size) : null;
        using FormattedText text = CreateText("AV glyph jog", size, pen);
        Rect actual = text.ActualBounds;
        Rect raster = text.RasterBounds;
        Assert.That(raster.IsEmpty, Is.False);

        var device = PixelRect.FromRect(raster, 1);
        using var surface = SKSurface.Create(
            new SKImageInfo(device.Width, device.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.Save();
            canvas.Translate(-device.X, -device.Y);
            if (useThickStroke)
            {
                canvas.DrawPath(
                    text.GetStrokePath()
                    ?? throw new InvalidOperationException("The thick-stroke fixture did not create a stroke path."),
                    paint);
            }
            else
            {
                canvas.DrawText(text.GetTextBlob(), 0, 0, paint);
            }

            canvas.Restore();
        }

        canvas.Flush();
        using SKImage image = surface.Snapshot();
        using SKBitmap bitmap = SKBitmap.FromImage(image);

        int touchedLeft = bitmap.Width;
        int touchedTop = bitmap.Height;
        int touchedRight = -1;
        int touchedBottom = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0)
                    continue;

                touchedLeft = Math.Min(touchedLeft, x);
                touchedTop = Math.Min(touchedTop, y);
                touchedRight = Math.Max(touchedRight, x);
                touchedBottom = y;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(touchedRight, Is.GreaterThanOrEqualTo(0),
                "the fixture must actually rasterize glyphs");
            Assert.That(touchedLeft, Is.GreaterThan(0),
                "a mask touching column 0 means RasterBounds did not leave room left of the glyphs");
            Assert.That(touchedTop, Is.GreaterThan(0),
                "a mask touching row 0 means RasterBounds did not leave room above the glyphs");
            Assert.That(touchedRight, Is.LessThan(bitmap.Width - 1),
                "a mask touching the last column means RasterBounds did not leave room right of the glyphs");
            Assert.That(touchedBottom, Is.LessThan(bitmap.Height - 1),
                "a mask touching the last row means RasterBounds did not leave room below the glyphs");
            Assert.That(raster.X, Is.LessThanOrEqualTo(actual.X));
            Assert.That(raster.Y, Is.LessThanOrEqualTo(actual.Y));
            Assert.That(raster.Right, Is.GreaterThanOrEqualTo(actual.Right));
            Assert.That(raster.Bottom, Is.GreaterThanOrEqualTo(actual.Bottom));
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
        Rect expectedActual = text.GetFillPath().TightBounds.ToGraphicsRect();

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(expectedActual),
                "ActualBounds must remain the semantic fill-path bounds without the raster apron.");
            Assert.That(raster.X, Is.LessThanOrEqualTo(actual.X));
            Assert.That(raster.Y, Is.LessThanOrEqualTo(actual.Y));
            Assert.That(raster.Right, Is.GreaterThanOrEqualTo(actual.Right));
            Assert.That(raster.Bottom, Is.GreaterThanOrEqualTo(actual.Bottom));
        });
    }

    [Test]
    public void Bounds_ExtremeNegativeSpacingNeverPublishesNegativeWidth()
    {
        using FormattedText text = CreateText("Spacing", 48f);
        text.Spacing = -10_000;

        Rect bounds = text.Bounds;

        Assert.Multiple(() =>
        {
            Assert.That(bounds.Width, Is.GreaterThanOrEqualTo(0));
            Assert.That(bounds.IsInvalid, Is.False);
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

    private static Pen.Resource CreateThickPen(float textSize)
    {
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.White;
        pen.Thickness.CurrentValue = MathF.Max(4, textSize / 3);
        pen.StrokeAlignment.CurrentValue = StrokeAlignment.Outside;
        return pen.ToResource(CompositionContext.Default);
    }
}
