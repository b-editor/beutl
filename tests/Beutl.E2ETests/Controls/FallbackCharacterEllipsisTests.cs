using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Beutl.Controls;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class FallbackCharacterEllipsisTests
{
    private const string LongName = "A font name too long for its row";

    // Bundled with Beutl. A color emoji font has no ellipsis.
    private static readonly Typeface s_withoutEllipsis = new("avares://Beutl.Controls/Assets/Fonts/NotoColorEmoji#Noto Color Emoji");

    // Bundled with Beutl, with an ellipsis, and not the UI font outside Korean.
    private static readonly Typeface s_withEllipsis = new("avares://Beutl.Controls/Assets/Fonts/NotoSansKR#Noto Sans KR");

    [AvaloniaTest]
    public void A_font_without_an_ellipsis_trims_with_the_ellipsis_of_the_UI_font()
    {
        (string family, ushort[] glyphs) = TrimmedEllipsis(s_withoutEllipsis);

        Assert.Multiple(() =>
        {
            Assert.That(family, Is.EqualTo(new Typeface(FontFamily.Default).GlyphTypeface.FamilyName));
            Assert.That(glyphs, Is.Not.Empty.And.None.EqualTo(0), "Glyph 0 is the box a font draws for a missing glyph.");
        });
    }

    [AvaloniaTest]
    public void A_font_with_an_ellipsis_keeps_its_own()
    {
        (string family, _) = TrimmedEllipsis(s_withEllipsis);

        Assert.That(family, Is.EqualTo(s_withEllipsis.GlyphTypeface.FamilyName));
    }

    // The family and glyphs a trimmed name draws its ellipsis with.
    private static (string Family, ushort[] Glyphs) TrimmedEllipsis(Typeface typeface)
    {
        using var layout = new TextLayout(LongName, typeface, 14, Brushes.Black,
            textTrimming: FallbackCharacterEllipsis.Instance, maxWidth: 80);
        ShapedTextRun ellipsis = layout.TextLines.Single().TextRuns.OfType<ShapedTextRun>().Last();
        Assert.That(ellipsis.Text.ToString(), Is.EqualTo(char.ConvertFromUtf32(0x2026)));
        return (ellipsis.GlyphRun.GlyphTypeface.FamilyName, [.. ellipsis.GlyphRun.GlyphInfos.Select(info => info.GlyphIndex)]);
    }
}
