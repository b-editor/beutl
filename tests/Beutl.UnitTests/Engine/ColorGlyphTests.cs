using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class ColorGlyphTests
{
    // Noto Emoji e20cbc2bbec1926686be9f9bee7d1d2cfa1fea0e, 2D/fonts/NotoColorEmoji.ttf.
    // Subset with fontTools: U+0020,U+1F600,U+1F601,U+1F469,U+200D,U+1F4BB, including GSUB closure.
    // Its PNG strikes are repackaged as sbix (bearingY - height gives originOffsetY), with two
    // single-point contours carrying each bitmap's bounds for CoreText. The renamed fixture is
    // covered by the adjacent NotoColorEmoji-LICENSE.txt.
    private static readonly FontFamily s_font = new("Beutl Test Color Emoji");

    [OneTimeSetUp]
    public void RegisterFonts() => _ = TypefaceProvider.Typeface();

    [Test]
    public void Fixture_HasVisibleColorGlyphsWithoutOutlines()
    {
        using FormattedText text = CreateText("😀");
        using SKFont font = text.ToSKFont();
        Assert.Multiple(() =>
        {
            Assert.That(font.Typeface.FamilyName, Is.EqualTo(s_font.Name));
            Assert.That(text.ToGeometries().Length, Is.EqualTo(1));
            Assert.That(text.GetFillPath().IsEmpty, Is.True);
            Assert.That(text.RasterBounds.IsEmpty, Is.False);
        });

        using var node = new TextRenderNode(text, Brushes.Resource.White, null);
        using var renderer = new RenderNodeRenderer(node, Request());
        using RenderNodeRasterization raster = renderer.Rasterize();
        Assert.That(CountColorPixels(raster), Is.GreaterThan(100));
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void TextBlock_SplitPreservesColorPixelsInMixedMultilineText(float scale)
    {
        int grouped = RenderTextBlock(false, scale);
        int split = RenderTextBlock(true, scale);
        Assert.Multiple(() =>
        {
            Assert.That(grouped, Is.GreaterThan(100));
            Assert.That(split, Is.EqualTo(grouped));
        });
    }

    private static int RenderTextBlock(bool split, float scale)
    {
        var block = new TextBlock
        {
            Text = { CurrentValue = "<font='Roboto'>I</font>😀 😁<font='Roboto'>I</font>\n👩‍💻" },
            FontFamily = { CurrentValue = s_font },
            Size = { CurrentValue = 64f },
            Spacing = { CurrentValue = 12f },
            SplitByCharacters = { CurrentValue = split },
        };
        using TextBlock.Resource resource = block.ToResource(CompositionContext.Default);
        using var node = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(node, new Size(400, 160)))
            block.Render(context, resource);

        using var renderer = new RenderNodeRenderer(node, Request(scale));
        using RenderNodeRasterization raster = renderer.Rasterize();
        return CountColorPixels(raster);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TextRenderNode_HitsColorGlyphOnlyWhenFilled(bool hasFill)
    {
        using FormattedText text = CreateText("😀");
        using var node = new TextRenderNode(text, hasFill ? Brushes.Resource.White : null, null);
        using var renderer = new RenderNodeRenderer(node, Request());
        Assert.That(renderer.HitTest(text.RasterBounds.Center), Is.EqualTo(hasFill));
    }

    [Test]
    public void HitTest_UsesIndividualGlyphBoundsAndExcludesSpaces()
    {
        using FormattedText text = CreateText("😀 😁");
        text.Spacing = 12f;
        FormattedText first = text.GetNonOutlineGlyph(0)!;
        FormattedText last = text.GetNonOutlineGlyph(2)!;
        Assert.That(text.GetNonOutlineGlyph(1), Is.Null, "A space has no ink.");
        using var node = new TextRenderNode(text, Brushes.Resource.White, null);
        using var renderer = new RenderNodeRenderer(node, Request());
        using var lastNode = new TextRenderNode(last, Brushes.Resource.White, null);
        using var lastRenderer = new RenderNodeRenderer(lastNode, Request());
        Assert.Multiple(() =>
        {
            Assert.That(renderer.HitTest(first.ActualBounds.Center), Is.True);
            Assert.That(renderer.HitTest(last.ActualBounds.Center), Is.True);
            Assert.That(renderer.HitTest(new Point(
                (first.ActualBounds.Right + last.ActualBounds.Left) / 2, first.ActualBounds.Center.Y)), Is.False);
            Assert.That(renderer.HitTest(new Point(last.ActualBounds.Right + 2, last.ActualBounds.Center.Y)), Is.False);
            Assert.That(lastRenderer.HitTest(first.ActualBounds.Center), Is.False);
            Assert.That(lastRenderer.HitTest(last.ActualBounds.Center), Is.True);
            Assert.That(text.ActualBounds.Contains(last.ActualBounds), Is.True);
        });
    }

    [Test]
    public void OutlineGlyphs_KeepTheirGeometryAndHitTesting()
    {
        using var text = new FormattedText { Text = "I", Font = new FontFamily("Roboto"), Size = 64f };
        using var node = new TextRenderNode(text, Brushes.Resource.White, null);
        using var renderer = new RenderNodeRenderer(node, Request());
        Assert.Multiple(() =>
        {
            Assert.That(text.GetNonOutlineGlyph(0), Is.Null);
            Assert.That(renderer.HitTest(text.ToGeometries()[0].Bounds.Center), Is.True);
        });
    }

    [TestCase("😀 😁", 0.5f)]
    [TestCase("😀 😁", 1f)]
    [TestCase("😀 😁", 2f)]
    [TestCase("👩‍💻😀", 0.5f)]
    [TestCase("👩‍💻😀", 1f)]
    [TestCase("👩‍💻😀", 2f)]
    public void SplitBlobs_PreserveShapedGlyphsAndPositions(string value, float scale)
    {
        using FormattedText text = CreateText(value);
        text.Spacing = 13f;
        using SKBitmap grouped = DrawBlobs(text, scale, false);
        using SKBitmap split = DrawBlobs(text, scale, true);
        Assert.That(split.Bytes, Is.EqualTo(grouped.Bytes));
        if (value.StartsWith("👩‍💻"))
            Assert.That(text.ToGeometries().Length, Is.EqualTo(2), "The ZWJ sequence must remain one shaped glyph.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RemeasureOrDispose_ReleasesSingleGlyphBlobs(bool dispose)
    {
        var text = CreateText("😀😁");
        try
        {
            FormattedText first = text.GetNonOutlineGlyph(0)!;
            FormattedText last = text.GetNonOutlineGlyph(1)!;
            SKTextBlob blob = first.GetTextBlob()!;
            SKTextBlob scaled = last.GetTextBlob(2f)!;
            Point oldCenter = first.ActualBounds.Center;
            if (dispose)
            {
                text.Dispose();
            }
            else
            {
                text.Text = " ";
                _ = text.Bounds;
                Assert.That(text.GetNonOutlineGlyph(0), Is.Null);
                Assert.That(text.NonOutlineContains(oldCenter), Is.False);
            }

            Assert.Multiple(() =>
            {
                Assert.That(first.IsDisposed, Is.True);
                Assert.That(last.IsDisposed, Is.True);
                Assert.That(blob.Handle, Is.EqualTo(IntPtr.Zero));
                Assert.That(scaled.Handle, Is.EqualTo(IntPtr.Zero));
            });
        }
        finally
        {
            text.Dispose();
        }
    }

    [Test]
    public void RepeatedGlyphs_HaveDistinctRenderIdentities()
    {
        using FormattedText text = CreateText("😀😀");
        FormattedText first = text.GetNonOutlineGlyph(0)!;
        FormattedText last = text.GetNonOutlineGlyph(1)!;
        using var node = new TextRenderNode(first, Brushes.Resource.White, null);
        Assert.Multiple(() =>
        {
            Assert.That(last, Is.Not.EqualTo(first));
            Assert.That(node.Update(last, Brushes.Resource.White, null), Is.True);
            Assert.That(text.GetNonOutlineGlyph(1), Is.SameAs(last));
        });
    }

    private static SKBitmap DrawBlobs(FormattedText text, float scale, bool split)
    {
        using var surface = SKSurface.Create(new SKImageInfo(600, 250));
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.Translate(8 * scale, 100 * scale);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        if (split)
        {
            for (int i = 0; i < text.ToGeometries().Length; i++)
            {
                if (text.GetNonOutlineGlyph(i) is { } glyph)
                    surface.Canvas.DrawText(glyph.GetTextBlob(scale), 0, 0, paint);
            }
        }
        else
        {
            surface.Canvas.DrawText(text.GetTextBlob(scale), 0, 0, paint);
        }

        using SKImage image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    private static FormattedText CreateText(string value) => new()
    {
        Font = s_font,
        Text = value,
        Size = 64f,
    };

    private static RenderNodeRenderRequest Request(float scale = 1f) => new()
    {
        Intent = RenderIntent.Preview,
        OutputScale = scale,
        CacheOptions = RenderCacheOptions.Disabled,
    };

    private static int CountColorPixels(RenderNodeRasterization raster)
    {
        if (raster.IsEmpty)
            return 0;

        using Bitmap bitmap = raster.Bitmap!.Convert(
            BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                int offset = x * 4;
                if (row[offset + 3] > 128 && row[offset + 2] > row[offset] + 32)
                    count++;
            }
        }

        return count;
    }
}
