using BEditorNext.Graphics;

using SkiaSharp;

namespace BEditorNext.Media;

internal static class FontExtensions
{
    public static SKTypeface ToSkia(this Typeface typeface)
    {
        return FontManager.Instance._fonts[typeface.FontFamily].Get(typeface);
    }

    public static Typeface ToTypeface(this SKTypeface typeface)
    {
        return new Typeface(new FontFamily(typeface.FamilyName), typeface.FontSlant.ToFontStyle(), (FontWeight)typeface.FontWeight);
    }
}
