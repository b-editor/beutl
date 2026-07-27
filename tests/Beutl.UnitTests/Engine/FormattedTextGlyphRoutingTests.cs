using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.UnitTests.Engine;

// FormattedText splits shaped glyphs by whether the font can supply an outline: outline glyphs
// become the resolution-independent fill path, the rest stay on Skia's glyph rasterizer as the
// colour-glyph blob. The bundled test fonts are outline-only, so the blob side is exercised through
// its degenerate (null) case; there is no colour font in tests/Beutl.UnitTests/Assets/Font.
[TestFixture]
public class FormattedTextGlyphRoutingTests
{
    [Test]
    public void OutlineFont_RoutesEveryGlyphToTheFillPath()
    {
        FormattedText text = CreateText();

        Assert.Multiple(() =>
        {
            Assert.That(text.GetFillPath().IsEmpty, Is.False, "outline glyphs must reach the fill path");
            Assert.That(text.GetColorGlyphBlob(), Is.Null,
                "an outline-only font must leave nothing for the glyph rasterizer");
            Assert.That(text.GetColorGlyphBlob(2f), Is.Null,
                "the density-scaled blob must agree with the logical one about which glyphs it owns");
        });
    }

    [Test]
    public void FillPath_IsDensityIndependent()
    {
        FormattedText text = CreateText();
        Rect bounds = text.Bounds;

        SKPath first = text.GetFillPath();
        _ = text.GetColorGlyphBlob(2f);
        SKPath second = text.GetFillPath();

        Assert.Multiple(() =>
        {
            Assert.That(ReferenceEquals(first, second), Is.True,
                "the fill path is measured once and scaled by the CTM, so a density access must not rebuild it");
            Assert.That(text.Bounds, Is.EqualTo(bounds), "a density access must not mutate the logical bounds");
        });
    }

    [Test]
    public void StrokePath_IsBuiltInLogicalUnits()
    {
        FormattedText text = CreateText();
        text.Pen = new Pen
        {
            Thickness = { CurrentValue = 2f },
            Brush = { CurrentValue = Brushes.White }
        }.ToResource(CompositionContext.Default);

        SKPath? stroke = text.GetStrokePath();

        Assert.That(stroke, Is.Not.Null);
        Assert.That(text.ActualBounds.Width, Is.GreaterThanOrEqualTo(text.Bounds.Width),
            "the stroke must widen the ink bounds it is measured into");
    }

    [Test]
    public void EmptyText_ProducesNoGlyphsOnEitherRoute()
    {
        FormattedText text = CreateText();
        text.Text = string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(text.GetFillPath().IsEmpty, Is.True);
            Assert.That(text.GetColorGlyphBlob(), Is.Null);
            Assert.That(text.GetColorGlyphBlob(2f), Is.Null);
        });
    }

    [Test]
    public void PropertyChange_RemeasuresTheFillPath()
    {
        FormattedText text = CreateText();
        float beforeWidth = text.GetFillPath().TightBounds.Width;

        text.Size = 48f;

        Assert.That(text.GetFillPath().TightBounds.Width, Is.GreaterThan(beforeWidth),
            "the larger font size must produce a wider fill path");
    }

    private static FormattedText CreateText()
    {
        Typeface typeface = TypefaceProvider.Typeface();
        return new FormattedText
        {
            Font = typeface.FontFamily,
            Style = typeface.Style,
            Weight = typeface.Weight,
            Size = 24f,
            Spacing = 1f,
            Text = "Scale"
        };
    }
}
