using SkiaSharp;

namespace BEditorNext.Graphics;

public readonly struct FontFamily : IEquatable<FontFamily>
{
    public static readonly FontFamily Default = new(SKTypeface.Default.FamilyName);

    internal FontFamily(string familyname)
    {
        Name = familyname;
    }

    public string Name { get; }

    public IEnumerable<Typeface> Typefaces
    {
        get
        {
            if (FontManager.Instance._fonts.TryGetValue(this, out TypefaceCollection? value))
            {
                return value.Keys;
            }
            else
            {
                return Enumerable.Empty<Typeface>();
            }
        }
    }

    public override bool Equals(object? obj)
    {
        return obj is FontFamily family && Equals(family);
    }

    public bool Equals(FontFamily other)
    {
        return Name == other.Name;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name);
    }

    public static bool operator ==(FontFamily left, FontFamily right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FontFamily left, FontFamily right)
    {
        return !(left == right);
    }
}
