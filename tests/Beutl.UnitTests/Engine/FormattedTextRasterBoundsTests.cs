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
        InkExtent ink = MeasureInk(device, (canvas, paint) =>
        {
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
        });

        Assert.Multiple(() =>
        {
            Assert.That(ink.Right, Is.GreaterThanOrEqualTo(0),
                "the fixture must actually rasterize glyphs");
            Assert.That(ink.Left, Is.GreaterThan(0),
                "a mask touching column 0 means RasterBounds did not leave room left of the glyphs");
            Assert.That(ink.Top, Is.GreaterThan(0),
                "a mask touching row 0 means RasterBounds did not leave room above the glyphs");
            Assert.That(ink.Right, Is.LessThan(device.Width - 1),
                "a mask touching the last column means RasterBounds did not leave room right of the glyphs");
            Assert.That(ink.Bottom, Is.LessThan(device.Height - 1),
                "a mask touching the last row means RasterBounds did not leave room below the glyphs");
            Assert.That(raster.X, Is.LessThanOrEqualTo(actual.X));
            Assert.That(raster.Y, Is.LessThanOrEqualTo(actual.Y));
            Assert.That(raster.Right, Is.GreaterThanOrEqualTo(actual.Right));
            Assert.That(raster.Bottom, Is.GreaterThanOrEqualTo(actual.Bottom));
        });
    }

    private static IEnumerable<TestCaseData> ScaledRasterBoundsCases()
    {
        foreach (float size in s_sizes)
        {
            foreach (float scale in new[] { 0.25f, 0.5f, 0.75f })
            {
                yield return new TestCaseData(size, scale)
                    .SetName($"GetRasterBounds_{size:g}_At{scale:g}_ContainsMaskWithFourSideHeadroom");
            }
        }
    }

    [TestCaseSource(nameof(ScaledRasterBoundsCases))]
    public void GetRasterBounds_ContainsEveryRasterizedGlyphPixelAtADownscale(float size, float scale)
    {
        using FormattedText text = CreateText("AV glyph jog", size);
        Rect raster = text.GetRasterBounds(scale);
        Assert.That(raster.IsEmpty, Is.False);

        var device = PixelRect.FromRect(raster, scale);
        InkExtent ink = MeasureInk(device, (canvas, paint) => canvas.DrawText(text.GetTextBlob(scale), 0, 0, paint));

        Assert.Multiple(() =>
        {
            Assert.That(ink.Right, Is.GreaterThanOrEqualTo(0),
                "the fixture must actually rasterize glyphs");
            Assert.That(ink.Left, Is.GreaterThan(0),
                "a mask touching column 0 means the downscaled footprint clips the glyphs on the left");
            Assert.That(ink.Top, Is.GreaterThan(0),
                "a mask touching row 0 means the downscaled footprint clips the glyphs above");
            Assert.That(ink.Right, Is.LessThan(device.Width - 1),
                "a mask touching the last column means the downscaled footprint clips the glyphs on the right");
            Assert.That(ink.Bottom, Is.LessThan(device.Height - 1),
                "a mask touching the last row means the downscaled footprint clips the glyphs below");
        });
    }

    [TestCase(0.25f)]
    [TestCase(0.5f)]
    [TestCase(0.75f)]
    [TestCase(2f)]
    public void GetRasterBounds_NeverNarrowsTheUnscaledFootprint(float scale)
    {
        using FormattedText text = CreateText("Your model", 48f);
        Rect raster = text.RasterBounds;
        Rect scaled = text.GetRasterBounds(scale);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scaled.X, Is.LessThanOrEqualTo(raster.X));
            Assert.That(scaled.Y, Is.LessThanOrEqualTo(raster.Y));
            Assert.That(scaled.Right, Is.GreaterThanOrEqualTo(raster.Right));
            Assert.That(scaled.Bottom, Is.GreaterThanOrEqualTo(raster.Bottom));
        }
    }

    [TestCase(1f)]
    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    public void GetRasterBounds_FallsBackToTheUnscaledFootprintForAScaleItCannotMeasure(float scale)
    {
        using FormattedText text = CreateText("Your model", 48f);

        Assert.That(text.GetRasterBounds(scale), Is.EqualTo(text.RasterBounds));
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

    // Draws onto a transparent surface that covers device and returns the inked pixels' extent, whose Right is
    // -1 when nothing was drawn.
    private static InkExtent MeasureInk(PixelRect device, Action<SKCanvas, SKPaint> draw)
    {
        using var surface = SKSurface.Create(
            new SKImageInfo(device.Width, device.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.Save();
            canvas.Translate(-device.X, -device.Y);
            draw(canvas, paint);
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

        return new InkExtent(touchedLeft, touchedTop, touchedRight, touchedBottom);
    }

    private static Pen.Resource CreateThickPen(float textSize)
    {
        var pen = new Pen();
        pen.Brush.Value = Brushes.White;
        pen.Thickness.Value = MathF.Max(4, textSize / 3);
        pen.StrokeAlignment.Value = StrokeAlignment.Outside;
        return pen.ToResource(CompositionContext.Default);
    }

    private readonly record struct InkExtent(int Left, int Top, int Right, int Bottom);
}
