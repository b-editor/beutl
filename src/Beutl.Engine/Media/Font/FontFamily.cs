using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Serialization;

using Beutl.Converters;

using SkiaSharp;

namespace Beutl.Media;

[JsonConverter(typeof(FontFamilyJsonConverter))]
[TypeConverter(typeof(FontFamilyConverter))]
public class FontFamily : IEquatable<FontFamily?>
{
    public static readonly FontFamily Default = new(GetDefaultFontFamily());

    public FontFamily(string familyname)
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

    public bool Equals(FontFamily? other)
    {
        return Name == other?.Name;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name);
    }

    private static string GetDefaultFontFamily()
    {
        if (OperatingSystem.IsLinux())
        {
            using Process process = Process.Start(new ProcessStartInfo("/usr/bin/fc-match", "--format %{family}")
            {
                RedirectStandardOutput = true
            })!;
            process.WaitForExit();

            return process.StandardOutput.ReadToEnd();
        }
        else
        {
            return SKTypeface.Default.FamilyName;
        }
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
