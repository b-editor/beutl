using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// This project is not a friend of <c>Beutl.Engine</c>, so a plugin that wants to animate text one glyph
/// at a time has to reach the per-glyph outlines through the same public surface these tests compile
/// against — shaping the run itself would drift from what <c>TextBlock</c> draws for the same string.
/// </summary>
[TestFixture]
public sealed class TextOutlineAuthoringContractTests
{
    private const string FontFamilyName = "Roboto";

    [OneTimeSetUp]
    public void RegisterTestFont()
    {
        using Stream? stream = typeof(TextOutlineAuthoringContractTests).Assembly
            .GetManifestResourceStream("Beutl.PublicApiContractTests.Roboto-Regular.ttf");
        Assert.That(stream, Is.Not.Null, "The linked contract-test font must be embedded.");
        FontManager.Instance.AddFont(stream);
        Assert.That(FontManager.Instance.GetTypefaces(new FontFamily(FontFamilyName)), Is.Not.Empty);
    }

    [Test]
    public void APluginCanObtainPerGlyphOutlinesFromFormattedText()
    {
        using FormattedText text = CreateText("AVA");

        IReadOnlyList<Geometry.Resource> glyphs = text.ToGeometries();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(glyphs, Has.Count.EqualTo(3), "One outline per shaped glyph.");
            Assert.That(glyphs, Has.None.Null);
        }

        Rect ink = Rect.Empty;
        float previousLeft = float.NegativeInfinity;
        for (int i = 0; i < glyphs.Count; i++)
        {
            Rect bounds = glyphs[i].Bounds;
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

    [Test]
    public void PerGlyphOutlines_AreBorrowedAndRewrittenByTheNextMeasure()
    {
        using FormattedText text = CreateText("I");

        Geometry.Resource narrow = text.ToGeometries()[0];
        float narrowWidth = narrow.Bounds.Width;

        text.Text = "W";
        IReadOnlyList<Geometry.Resource> afterRemeasure = text.ToGeometries();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterRemeasure[0], Is.SameAs(narrow),
                "The list is engine-owned and rewritten in place, so a retained entry is not a snapshot.");
            Assert.That(afterRemeasure[0].Bounds.Width, Is.GreaterThan(narrowWidth),
                "The retained entry now carries the newly measured glyph.");
        }

        text.Text = "WWW";
        Assert.That(text.ToGeometries(), Has.Count.EqualTo(3));

        text.Text = "W";
        Assert.That(text.ToGeometries(), Has.Count.EqualTo(1),
            "A shorter measure truncates the borrowed list.");
    }

    [Test]
    public void PerGlyphOutlines_AreUnreachableOnceTheTextIsDisposed()
    {
        FormattedText text = CreateText("A");
        text.Dispose();

        Assert.That(text.ToGeometries, Throws.TypeOf<ObjectDisposedException>());
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
