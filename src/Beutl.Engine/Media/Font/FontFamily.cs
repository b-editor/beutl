using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Serialization;

using Beutl.Converters;

using SkiaSharp;

namespace Beutl.Media;

[JsonConverter(typeof(FontFamilyJsonConverter))]
[TypeConverter(typeof(FontFamilyConverter))]
public class FontFamily(string familyname) : IEquatable<FontFamily?>
{
    public static readonly FontFamily Default = new(GetDefaultFontFamily());

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
