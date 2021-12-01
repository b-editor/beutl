namespace BEditorNext.Graphics;

public readonly struct Typeface : IEquatable<Typeface>
{
    public Typeface(FontFamily fontFamily,
        FontStyle style = FontStyle.Normal,
        FontWeight weight = FontWeight.Regular)
    {
        FontFamily = fontFamily;
        Style = style;
        Weight = weight;
    }

    /// <summary>
    /// Gets the font family.
    /// </summary>
    public FontFamily FontFamily { get; }

    /// <summary>
    /// Gets the font style.
    /// </summary>
    public FontStyle Style { get; }

    /// <summary>
    /// Gets the font weight.
    /// </summary>
    public FontWeight Weight { get; }

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
