using Beutl.Graphics;

namespace Beutl.Media.TextFormatting;

public record struct FormattedTextInfo(Typeface Typeface, float Size, IBrush? Brush, float Space, Thickness Margin)
{
    public static readonly FormattedTextInfo Default
        = new(new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Regular), 24, Brushes.White, 0, default);

    public FormattedTextInfo(Typeface Typeface, float Size, Color Color, float Space, Thickness Margin)
        : this(Typeface, Size, Color.ToImmutableBrush(), Space, Margin)
    {
    }
}
