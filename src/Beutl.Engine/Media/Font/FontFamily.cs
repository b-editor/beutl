using System.ComponentModel;
using System.Text.Json.Serialization;

using Beutl.Converters;

namespace Beutl.Media;

[JsonConverter(typeof(FontFamilyJsonConverter))]
[TypeConverter(typeof(FontFamilyConverter))]
public class FontFamily(string familyname) : IEquatable<FontFamily?>
{
    public static FontFamily Default => FontManager.Instance.DefaultTypeface.FontFamily;

    public string Name { get; } = familyname;

    public IEnumerable<Typeface> Typefaces => FontManager.Instance.GetTypefaces(this);

    public override bool Equals(object? obj)
    {
        return obj is FontFamily family && Equals(family);
    }

    public bool Equals(FontFamily? other)
    {
        return Name == other?.Name;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name);
    }

    public static bool operator ==(FontFamily? left, FontFamily? right)
    {
        return left?.Name == right?.Name;
    }

    public static bool operator !=(FontFamily? left, FontFamily? right)
    {
        return !(left == right);
    }
}
