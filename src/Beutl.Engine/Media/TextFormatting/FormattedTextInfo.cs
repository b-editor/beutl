using Beutl.Graphics;

namespace Beutl.Media.TextFormatting;

public record struct FormattedTextInfo(Typeface Typeface, float Size, IBrush? Brush, float Space, IPen? Pen)
{
    public static readonly FormattedTextInfo Default
        = new(new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Regular), 24, Brushes.White, 0, null);

    public FormattedTextInfo(Typeface Typeface, float Size, Color Color, float Space, IPen? Pen)
        : this(Typeface, Size, Color.ToImmutableBrush(), Space, Pen)
    {
    }
}
