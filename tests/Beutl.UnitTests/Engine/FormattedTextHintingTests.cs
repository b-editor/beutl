using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class FormattedTextHintingTests
{
    // Hinted glyphs are refit to the pixel grid at each size, which made a scaling TextBlock flicker
    // on Linux (FreeType) about seven times as much as unhinted text (b-editor/beutl#2693).
    [Test]
    public void ToSKFont_DisablesHinting()
    {
        using var text = new FormattedText
        {
            Font = TypefaceProvider.Typeface().FontFamily,
            Size = 28,
            Text = "Beutl",
        };

        using SKFont font = text.ToSKFont();
        using SKFont dense = text.ToSKFont(2.5f);

        Assert.Multiple(() =>
        {
            Assert.That(font.Hinting, Is.EqualTo(SKFontHinting.None));
            Assert.That(dense.Hinting, Is.EqualTo(SKFontHinting.None));
        });
    }
}
