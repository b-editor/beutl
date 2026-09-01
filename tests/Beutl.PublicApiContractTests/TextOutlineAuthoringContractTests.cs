using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class TextOutlineAuthoringContractTests
{
    private const string FontFamilyName = "Roboto";

    [OneTimeSetUp]
    public void RegisterTestFont()
    {
        using Stream stream = typeof(TextOutlineAuthoringContractTests).Assembly
                .GetManifestResourceStream("Beutl.PublicApiContractTests.Roboto-Regular.ttf")
            ?? throw new AssertionException("The linked contract-test font must be embedded.");
        FontManager.Instance.AddFont(stream);
        Assert.That(FontManager.Instance.GetTypefaces(new FontFamily(FontFamilyName)), Is.Not.Empty);
    }

    [Test]
    public void APluginCanObtainPerGlyphOutlinesFromFormattedText()
    {
        using FormattedText text = CreateText("AVA");

        ReadOnlySpan<Geometry.Resource> glyphs = text.ToGeometries();

        Assert.That(glyphs.Length, Is.EqualTo(3), "One outline per shaped glyph.");

        Rect ink = Rect.Empty;
        float previousLeft = float.NegativeInfinity;
        for (int i = 0; i < glyphs.Length; i++)
        {
            Geometry.Resource glyph = glyphs[i];
            Assert.That(glyph, Is.Not.Null, $"Glyph {i} must be a live entry.");

            Rect bounds = glyph.Bounds;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(bounds.Width, Is.GreaterThan(0), $"Glyph {i} must carry a real outline.");
                Assert.That(bounds.Height, Is.GreaterThan(0), $"Glyph {i} must carry a real outline.");
                Assert.That(bounds.Left, Is.GreaterThan(previousLeft),
                    $"Glyph {i} must sit after its predecessor along the run.");
            }

            previousLeft = bounds.Left;
            ink = ink.IsEmpty ? bounds : ink.Union(bounds);
        }

        using (Assert.EnterMultipleScope())
        {
            // The outlines arrive already placed by the engine's own shaping, so their union is the ink the
            // whole run reports. A plugin that re-measured the text itself could not promise that.
            Assert.That(ink.Left, Is.EqualTo(text.ActualBounds.Left).Within(0.01f));
            Assert.That(ink.Top, Is.EqualTo(text.ActualBounds.Top).Within(0.01f));
            Assert.That(ink.Width, Is.EqualTo(text.ActualBounds.Width).Within(0.01f));
            Assert.That(ink.Height, Is.EqualTo(text.ActualBounds.Height).Within(0.01f));
            Assert.That(ink.Width, Is.LessThanOrEqualTo(text.Bounds.Width + 0.01f),
                "The ink of the glyphs stays inside the advance width of the run.");
        }
    }

    // The borrow is lexical, not documented: because ToGeometries() hands back a ref struct, the compiler
    // rejects storing it in a field or array, returning it, capturing it in a lambda, or awaiting across
    // it. So there is no "capture it and watch it go stale" case left to assert — only that the whole
    // borrow is readable inside one scope, which stops compiling if the return type ever becomes a
    // heap-reachable collection again.
    [Test]
    public void PerGlyphOutlines_MustBeConsumedWithinTheBorrowingScope()
    {
        using FormattedText text = CreateText("AVA");

        int glyphCount = 0;
        float widestGlyph = 0;
        foreach (Geometry.Resource glyph in text.ToGeometries())
        {
            glyphCount++;
            widestGlyph = MathF.Max(widestGlyph, glyph.Bounds.Width);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(glyphCount, Is.EqualTo(3), "The whole borrow is readable inside one scope.");
            Assert.That(widestGlyph, Is.GreaterThan(0));
        }
    }

    [Test]
    public void PerGlyphOutlines_AreBorrowedAndRewrittenByTheNextMeasure()
    {
        using FormattedText text = CreateText("I");

        // A single entry may still be copied out of the span — it is a reference, not the borrow — and
        // that copy is what the engine recycles, so it is the hazard the span shape narrows down to.
        Geometry.Resource narrow = text.ToGeometries()[0];
        float narrowWidth = narrow.Bounds.Width;

        text.Text = "W";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.ToGeometries()[0], Is.SameAs(narrow),
                "The entries are engine-owned and rewritten in place, so a retained entry is not a snapshot.");
            Assert.That(text.ToGeometries()[0].Bounds.Width, Is.GreaterThan(narrowWidth),
                "The retained entry now carries the newly measured glyph.");
        }

        text.Text = "WWW";
        Assert.That(text.ToGeometries().Length, Is.EqualTo(3));

        text.Text = "W";
        Assert.That(text.ToGeometries().Length, Is.EqualTo(1),
            "A shorter measure truncates the borrow.");
    }

    [Test]
    public void PerGlyphOutlines_AreUnreachableOnceTheTextIsDisposed()
    {
        FormattedText text = CreateText("A");
        text.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = text.ToGeometries().Length);
    }

    private static FormattedText CreateText(string value)
    {
        return new FormattedText
        {
            Font = new FontFamily(FontFamilyName),
            Style = FontStyle.Normal,
            Weight = FontWeight.Regular,
            Size = 64,
            Text = value,
        };
    }
}
