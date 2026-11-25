using Beutl.Composition;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.UnitTests.Engine;

[NonParallelizable]
public class TextPartLayoutTests
{
    private static readonly CompositionContext s_context = new(MediaTime.Zero) { Fonts = TestFonts.Manager };

    [OneTimeSetUp]
    public void RegisterFonts()
    {
        _ = TypefaceProvider.Typeface();
        using var stream = typeof(TextPartLayoutTests).Assembly.GetManifestResourceStream("NotoColorEmoji.ttf")!;
        TestFonts.Manager.AddFont(stream);
    }

    [Test]
    public void WordsContinueAcrossStylesAndEndAtWhitespace()
    {
        using var text = Create("A<b>B</b>, \u00A0C").ToResource(s_context);

        Assert.That(text.GetTextPartLayout().Counts, Is.EqualTo(new TextPartCounts(4, 2, 1)));
        Assert.That(GetGlyphs(text), Is.EqualTo(new TextPartIndices[]
        {
            new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(-1, -1, 0), new(-1, -1, 0), new(3, 1, 0),
        }));
    }

    [Test]
    public void UnspacedCjkAndPunctuationAreOneWord()
    {
        using var text = Create("日本語。").ToResource(s_context);

        Assert.That(text.GetTextPartLayout().Counts, Is.EqualTo(new TextPartCounts(4, 1, 1)));
        Assert.That(GetGlyphs(text), Is.EqualTo(new TextPartIndices[]
        {
            new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(3, 0, 0),
        }));
    }

    [Test]
    public void CrLfAndBlankLinesFollowHorizontalLayout()
    {
        using var text = Create("A\r\n\r\nB\n").ToResource(s_context);

        Assert.That(text.GetTextPartLayout().Counts, Is.EqualTo(new TextPartCounts(2, 2, 3)));
        Assert.That(GetGlyphs(text), Is.EqualTo(new TextPartIndices[] { new(0, 0, 0), new(1, 1, 2) }));
    }

    [Test]
    public void LigatureTakesOneCharacterSlot()
    {
        var block = Create("ffiX");
        block.FontFamily.Value = new FontFamily("Roboto");
        using var text = block.ToResource(s_context);
        var lines = text.GetTextElements().Lines.GetEnumerator();
        Assert.That(lines.MoveNext(), Is.True);

        Assert.That(lines.Current[0].GetGlyphParts().ToArray().Select(glyph => glyph.Cluster), Is.EqualTo(new[] { 0, 3 }));
        Assert.That(GetGlyphs(text), Is.EqualTo(new TextPartIndices[] { new(0, 0, 0), new(1, 0, 0) }));
    }

    [Test]
    public void CombiningMarkSharesItsBaseCharacterSlot()
    {
        using var text = Create("x\u0301x").ToResource(s_context);

        Assert.That(text.GetTextPartLayout().Counts, Is.EqualTo(new TextPartCounts(2, 1, 1)));
        Assert.That(GetGlyphs(text), Is.EqualTo(new TextPartIndices[] { new(0, 0, 0), new(0, 0, 0), new(1, 0, 0) }));
    }

    [Test]
    public void RtlFallbackGlyphsRetainLogicalOffsets()
    {
        var (families, emoji) = TestFonts.Manager.GetFallbackFamilies();
        TestFonts.Manager.SetFallbackFonts([TypefaceProvider.Typeface().FontFamily], new FontFamily("Noto Color Emoji"));
        try
        {
            using var text = Create("א😀😁").ToResource(s_context);
            var lines = text.GetTextElements().Lines.GetEnumerator();
            Assert.That(lines.MoveNext(), Is.True);

            Assert.That(lines.Current[0].GetGlyphParts().ToArray().Select(glyph => glyph.Cluster), Is.EqualTo(new[] { 3, 1, 0 }));
            Assert.That(GetGlyphs(text), Is.EqualTo(new TextPartIndices[] { new(2, 0, 0), new(1, 0, 0), new(0, 0, 0) }));
        }
        finally
        {
            TestFonts.Manager.SetFallbackFonts(families, emoji);
        }
    }

    [Test]
    public void VerticalColumnsKeepSourceSlicesAndWordsAcrossOrientations()
    {
        var block = Create("日AB。\n\nC\n");
        block.WritingMode.Value = WritingMode.VerticalRl;
        using var text = block.ToResource(s_context);
        var columns = text.GetVerticalLayout().Columns;
        var layout = text.GetTextPartLayout();

        Assert.That(layout.Counts, Is.EqualTo(new TextPartCounts(5, 2, 4)));
        Assert.That(columns[0].Runs.Select(run => run.Text.Text.Start), Is.EqualTo(new[] { 0, 1, 3 }));
        Assert.That(columns[0].Runs.SelectMany(run => layout.GetGlyphs(run.Text).ToArray()), Is.EqualTo(new TextPartIndices[]
        {
            new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(3, 0, 0),
        }));
        Assert.That(layout.GetGlyphs(columns[2].Runs.Single().Text).ToArray(), Is.EqualTo(new[] { new TextPartIndices(4, 1, 2) }));
    }

    [Test]
    public void SnapshotIsReusedAndInvalidatedByTextFontAndWritingMode()
    {
        var block = Create("AB");
        using var text = block.ToResource(s_context);
        TextPartLayout first = text.GetTextPartLayout();
        Assert.That(text.GetTextPartLayout(), Is.SameAs(first));

        bool versionBumped = false;
        block.Text.Value = "A B";
        text.Reconcile(block, s_context, ref versionBumped);
        TextPartLayout edited = text.GetTextPartLayout();
        Assert.That(edited.Revision, Is.GreaterThan(first.Revision));
        Assert.That(edited.Counts, Is.EqualTo(new TextPartCounts(2, 2, 1)));

        block.FontFamily.Value = new FontFamily("Roboto");
        text.Reconcile(block, s_context, ref versionBumped);
        TextPartLayout newFont = text.GetTextPartLayout();
        Assert.That(newFont.Revision, Is.GreaterThan(edited.Revision));

        block.WritingMode.Value = WritingMode.VerticalRl;
        text.Reconcile(block, s_context, ref versionBumped);
        TextPartLayout vertical = text.GetTextPartLayout();
        Assert.That(vertical.Revision, Is.GreaterThan(newFont.Revision));
        Assert.That(vertical.WritingMode, Is.EqualTo(WritingMode.VerticalRl));
    }

    private static TextPartIndices[] GetGlyphs(TextBlock.Resource text)
    {
        var layout = text.GetTextPartLayout();
        var glyphs = new List<TextPartIndices>();
        foreach (var line in text.GetTextElements().Lines)
            foreach (var run in line)
                glyphs.AddRange(layout.GetGlyphs(run).ToArray());
        return glyphs.ToArray();
    }

    private static TextBlock Create(string content) => new()
    {
        Text = { Value = content },
        FontFamily = { Value = TypefaceProvider.Typeface().FontFamily },
        Size = { Value = 40 },
        SplitByCharacters = { Value = true },
    };
}
