using BEditorNext.Graphics;

namespace BEditorNext.Media.TextFormatting;

public record struct FormattedTextInfo(Typeface Typeface, float Size, Color Color, float Space, Thickness Margin)
{
    public static readonly FormattedTextInfo Default
        = new(new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Regular), 24, Colors.White, 0, default);
}
