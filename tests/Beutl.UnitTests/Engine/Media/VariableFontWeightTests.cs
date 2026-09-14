using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Beutl.UnitTests.Engine.Media;

// Assets/Font/BeutlTestVariable.ttf ("Beutl Test Variable") has a single wght axis, 100..400..900. Its "I" is a
// bar whose stem is 40, 120 and 400 units wide at wght 100, 400 and 900, interpolated in between, and whose
// advance is 200 units plus the stem: 240, 320 and 600 units, and 488 units at wght 700.
[TestFixture]
public class VariableFontWeightTests
{
    private static readonly FontFamily s_variable = new("Beutl Test Variable");
    private static readonly SKFourByteTag s_wght = new('w', 'g', 'h', 't');

    [OneTimeSetUp]
    public void RegisterFonts()
    {
        _ = TypefaceProvider.Typeface();
    }

    [TestCase(FontWeight.Thin, 100f)]
    [TestCase(FontWeight.SemiLight, 350f)]
    [TestCase(FontWeight.Bold, 700f)]
    [TestCase(FontWeight.UltraBlack, 900f)]
    public void ResolveSkia_MovesAVariableFontToTheRequestedWeight(FontWeight weight, float expected)
    {
        SKTypeface resolved = new Typeface(s_variable, FontStyle.Normal, weight).ToSkia();

        Assert.That(WeightOf(resolved), Is.EqualTo(expected));
    }

    [Test]
    public void ResolveSkia_ReturnsTheRegisteredFace_AtItsOwnWeight()
    {
        SKTypeface registered = FontManager.Instance._fonts[s_variable].Values.Single();

        Assert.That(new Typeface(s_variable).ToSkia(), Is.SameAs(registered));
    }

    [Test]
    public void ResolveSkia_ReusesOneInstancePerRenderedWeight()
    {
        SKTypeface black = new Typeface(s_variable, FontStyle.Normal, FontWeight.Black).ToSkia();

        Assert.Multiple(() =>
        {
            Assert.That(new Typeface(s_variable, FontStyle.Normal, FontWeight.Black).ToSkia(), Is.SameAs(black));
            // The axis stops at 900, so UltraBlack renders the same instance as Black.
            Assert.That(new Typeface(s_variable, FontStyle.Normal, FontWeight.UltraBlack).ToSkia(), Is.SameAs(black));
        });
    }

    [Test]
    public void ResolveSkia_LeavesStaticFontsAtTheNearestRegisteredFace()
    {
        var roboto = new FontFamily("Roboto");
        SKTypeface medium = FontManager.Instance._fonts[roboto][new Typeface(roboto, FontStyle.Normal, FontWeight.Medium)];

        SKTypeface resolved = new Typeface(roboto, FontStyle.Normal, FontWeight.Bold).ToSkia();

        Assert.That(resolved, Is.SameAs(medium));
    }

    [TestCase("Noto Sans JP", "こんにちは、世界！ Hello", 1f)]
    [TestCase("Roboto", "office affine AVAWAY Ta fi", 1f)]
    [TestCase("Roboto", "é مرحبا 👍🏽", 1.25f)]
    public void TextShaper_ShapesAStaticFontExactlyAsSKShaper(string family, string text, float scaleX)
    {
        using var font = new SKFont(new Typeface(new FontFamily(family)).ToSkia(), 37.5f) { ScaleX = scaleX };

        SKShaper.Result expected = Shape(text, buffer =>
        {
            using var shaper = new SKShaper(font.Typeface);
            return shaper.Shape(buffer, font);
        });
        SKShaper.Result actual = Shape(text, buffer =>
        {
            using var shaper = new TextShaper(font.Typeface);
            return shaper.Shape(buffer, font);
        });

        Assert.Multiple(() =>
        {
            Assert.That(actual.Codepoints, Is.EqualTo(expected.Codepoints));
            Assert.That(actual.Clusters, Is.EqualTo(expected.Clusters));
            Assert.That(actual.Points, Is.EqualTo(expected.Points));
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
        });
    }

    [TestCase(FontWeight.Thin)]
    [TestCase(FontWeight.SemiLight)]
    [TestCase(FontWeight.Regular)]
    [TestCase(FontWeight.Bold)]
    [TestCase(FontWeight.Black)]
    public void TextShaper_AdvancesByTheWeightTheTypefaceRenders(FontWeight weight)
    {
        const string Text = "IIII";
        using var font = new SKFont(new Typeface(s_variable, FontStyle.Normal, weight).ToSkia(), 100f)
        {
            Subpixel = true,
            LinearMetrics = true,
            Hinting = SKFontHinting.None
        };

        SKShaper.Result result = Shape(Text, buffer =>
        {
            using var shaper = new TextShaper(font.Typeface);
            return shaper.Shape(buffer, font);
        });

        // Skia's advances come from the same instance it draws; HarfBuzz rounds each advance to 1/512 em.
        Assert.That(result.Width, Is.EqualTo(font.MeasureText(Text)).Within(Text.Length * font.Size / 1024f));
    }

    [TestCase(FontWeight.Regular, 320f)]
    [TestCase(FontWeight.Bold, 488f)]
    public void FormattedText_LaysOutTheRequestedWeight(FontWeight weight, float advanceUnits)
    {
        using var text = new FormattedText { Font = s_variable, Weight = weight, Size = 100f, Text = "II" };

        // At 100 px per em a unit is 0.1 px, and HarfBuzz rounds each advance to 1/512 em.
        Assert.That(text.Bounds.Width, Is.EqualTo(2 * advanceUnits / 10f).Within(2 * 100f / 1024f));
    }

    [Test]
    public void FormattedText_DrawsTheOutlinesOfTheRequestedWeight()
    {
        using var regular = new FormattedText { Font = s_variable, Weight = FontWeight.Regular, Size = 100f, Text = "II" };
        using var bold = new FormattedText { Font = s_variable, Weight = FontWeight.Bold, Size = 100f, Text = "II" };

        // From the left edge of the first stem to the right edge of the second: 32 + 12 = 44 px at wght 400,
        // and 48.8 + 28.8 = 77.6 px at wght 700.
        Assert.Multiple(() =>
        {
            Assert.That(regular.ActualBounds.Width, Is.EqualTo(44f).Within(1f));
            Assert.That(bold.ActualBounds.Width, Is.EqualTo(77.6f).Within(1f));
        });
    }

    private static float WeightOf(SKTypeface typeface)
    {
        return typeface.VariationDesignPosition.Single(coordinate => coordinate.Axis.Equals(s_wght)).Value;
    }

    private static SKShaper.Result Shape(string text, Func<HarfBuzzSharp.Buffer, SKShaper.Result> shape)
    {
        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();
        return shape(buffer);
    }
}
