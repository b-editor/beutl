using System.Text.Json.Serialization;

using Beutl.Converters;
using Beutl.Graphics;

using SkiaSharp;

namespace Beutl.Media;

[JsonConverter(typeof(TypefaceJsonConverter))]
public readonly struct Typeface(
    FontFamily fontFamily,
    FontStyle style = FontStyle.Normal,
    FontWeight weight = FontWeight.Regular) : IEquatable<Typeface>
{

    /// <summary>
    /// Gets the font family.
    /// </summary>
    public FontFamily FontFamily { get; } = fontFamily;

    /// <summary>
    /// Gets the font style.
    /// </summary>
    public FontStyle Style { get; } = style;

    /// <summary>
    /// Gets the font weight.
    /// </summary>
    public FontWeight Weight { get; } = weight;

    internal static Typeface FromSKTypeface(SKTypeface typeface)
    {
        return new Typeface(new FontFamily(typeface.FamilyName), typeface.FontSlant.ToFontStyle(), (FontWeight)typeface.FontWeight);
    }

    internal SKTypeface ToSkia()
    {
        return FontManager.Instance._fonts[FontFamily].Get(this);
    }

    public override bool Equals(object? obj)
    {
        return obj is Typeface typeface && Equals(typeface);
    }

    public bool Equals(Typeface other)
    {
        return EqualityComparer<FontFamily>.Default.Equals(FontFamily, other.FontFamily) && Style == other.Style && Weight == other.Weight;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(FontFamily, Style, Weight);
    }

    public static bool operator ==(Typeface left, Typeface right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Typeface left, Typeface right)
    {
        return !(left == right);
    }
}
