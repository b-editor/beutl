using System.ComponentModel;
using System.Text.Json.Serialization;
using Beutl.Converters;
using SkiaSharp;

namespace Beutl.Media;

[JsonConverter(typeof(FontFamilyJsonConverter))]
[TypeConverter(typeof(FontFamilyConverter))]
public class FontFamily(string familyname) : IEquatable<FontFamily?>
{
    private static readonly Lazy<FontFamily> s_default = new(() =>
    {
        SKTypeface face = OperatingSystem.IsLinux() ? DefaultFontResolver.Resolve() : SKTypeface.Default;
        try
        {
            return new FontFamily(face.FamilyName);
        }
        finally
        {
            if (!ReferenceEquals(face, SKTypeface.Default))
                face.Dispose();
        }
    });

    public static FontFamily Default => s_default.Value;

    public string Name { get; } = familyname;

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
