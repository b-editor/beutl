using Beutl.Graphics;
using Beutl.Media.TextFormatting;

using SkiaSharp;

namespace Beutl.Media;

internal static class FontExtensions
{
    public static SKTypeface ToSkia(this Typeface typeface)
    {
        return FontManager.Instance._fonts[typeface.FontFamily].Get(typeface);
    }

    public static SKFont ToSKFont(this FormattedText text)
    {
        var typeface = new Typeface(text.Font, text.Style, text.Weight);
        var font = new SKFont(typeface.ToSkia(), text.Size)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
            Hinting = SKFontHinting.Full
        };

        return font;
    }

    public static Typeface ToTypeface(this SKTypeface typeface)
    {
        return new Typeface(new FontFamily(typeface.FamilyName), typeface.FontSlant.ToFontStyle(), (FontWeight)typeface.FontWeight);
    }
}
