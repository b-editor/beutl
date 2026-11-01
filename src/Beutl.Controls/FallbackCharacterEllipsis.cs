using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Beutl.Controls;

// Trims the end of a line as TextTrimming.CharacterEllipsis does. Avalonia shapes that ellipsis in the line's typeface
// without falling back, so a font without U+2026 draws it as tofu; this one draws it in the UI font then.
internal sealed class FallbackCharacterEllipsis : TextTrimming
{
    private const string Ellipsis = "\u2026";

    public static FallbackCharacterEllipsis Instance { get; } = new();

    public override TextCollapsingProperties CreateCollapsingProperties(TextCollapsingCreateInfo createInfo)
    {
        TextRunProperties properties = createInfo.TextRunProperties;
        Typeface typeface = properties.Typeface;
        if (!FontManager.Current.TryGetGlyphTypeface(typeface, out GlyphTypeface? glyphTypeface)
            || !glyphTypeface.CharacterToGlyphMap.ContainsGlyph(Ellipsis[0]))
        {
            properties = new GenericTextRunProperties(
                new Typeface(FontFamily.Default, typeface.Style, typeface.Weight, typeface.Stretch),
                properties.FontRenderingEmSize, properties.TextDecorations, properties.ForegroundBrush,
                properties.BackgroundBrush, properties.BaselineAlignment, properties.CultureInfo, properties.FontFeatures);
        }

        return new TextTrailingCharacterEllipsis(Ellipsis, createInfo.Width, properties, createInfo.FlowDirection);
    }
}
