using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

using Beutl.Converters;

namespace Beutl.Media;

/// <summary>
/// An ARGB color.
/// </summary>
[JsonConverter(typeof(ColorJsonConverter))]
[TypeConverter(typeof(ColorConverter))]
public readonly struct Color(byte a, byte r, byte g, byte b)
    : IEquatable<Color>,
      IParsable<Color>,
      ISpanParsable<Color>
{

    /// <summary>
    /// Gets the Alpha component of the color.
    /// </summary>
    public byte A { get; } = a;

    /// <summary>
    /// Gets the Red component of the color.
    /// </summary>
    public byte R { get; } = r;

    /// <summary>
    /// Gets the Green component of the color.
    /// </summary>
    public byte G { get; } = g;

    /// <summary>
    /// Gets the Blue component of the color.
    /// </summary>
    public byte B { get; } = b;

    /// <summary>
    /// Creates a <see cref="Color"/> from alpha, red, green and blue components.
    /// </summary>
    /// <param name="a">The alpha component.</param>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <returns>The color.</returns>
    public static Color FromArgb(byte a, byte r, byte g, byte b)
    {
        return new Color(a, r, g, b);
    }

    /// <summary>
    /// Creates a <see cref="Color"/> from red, green and blue components.
    /// </summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <returns>The color.</returns>
    public static Color FromRgb(byte r, byte g, byte b)
    {
        return new Color(0xff, r, g, b);
    }

    /// <summary>
    /// Creates a <see cref="Color"/> from an integer.
    /// </summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The color.</returns>
    public static Color FromUInt32(uint value)
    {
        return new Color(
            (byte)(value >> 24 & 0xff),
            (byte)(value >> 16 & 0xff),
            (byte)(value >> 8 & 0xff),
            (byte)(value & 0xff)
        );
    }

    /// <summary>
    /// Creates a <see cref="Color"/> from an integer.
    /// </summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The color.</returns>
    public static Color FromInt32(int value)
    {
        return new Color(
            (byte)(value >> 24 & 0xff),
            (byte)(value >> 16 & 0xff),
            (byte)(value >> 8 & 0xff),
            (byte)(value & 0xff)
        );
    }

    /// <summary>
    /// Parses a color string.
    /// </summary>
    /// <param name="s">The color string.</param>
    /// <returns>The <see cref="Color"/>.</returns>
    public static Color Parse(string s)
    {
        if (s is null)
        {
            throw new ArgumentNullException(nameof(s));
        }

        if (TryParse(s, out Color color))
        {
            return color;
        }

        throw new FormatException($"Invalid color string: '{s}'.");
    }

    /// <summary>
    /// Parses a color string.
    /// </summary>
    /// <param name="s">The color string.</param>
    /// <returns>The <see cref="Color"/>.</returns>
    public static Color Parse(ReadOnlySpan<char> s)
    {
        if (TryParse(s, out Color color))
        {
            return color;
        }

        throw new FormatException($"Invalid color string: '{s.ToString()}'.");
    }

    /// <summary>
    /// Parses a color string.
    /// </summary>
    /// <param name="s">The color string.</param>
    /// <param name="color">The parsed color</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(string s, out Color color)
    {
        color = default;

        if (s is null)
        {
            return false;
        }

        if (s.Length == 0)
        {
            return false;
        }

        if (s[0] == '#' && TryParseInternal(s.AsSpan(), out color))
        {
            return true;
        }

        KnownColor knownColor = KnownColors.GetKnownColor(s);

        if (knownColor != KnownColor.None)
        {
            color = knownColor.ToColor();

            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses a color string.
    /// </summary>
    /// <param name="s">The color string.</param>
    /// <param name="color">The parsed color</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out Color color)
    {
        color = default;

        if (s.Length == 0)
        {
            return false;
        }

        if (s[0] == '#')
        {
            return TryParseInternal(s, out color);
        }

        KnownColor knownColor = KnownColors.GetKnownColor(s.ToString());

        if (knownColor != KnownColor.None)
        {
            color = knownColor.ToColor();

            return true;
        }

        return false;
    }

    private static bool TryParseInternal(ReadOnlySpan<char> s, out Color color)
    {
        static bool TryParseCore(ReadOnlySpan<char> input, ref Color color)
        {
            uint alphaComponent = 0u;

            if (input.Length == 6)
            {
                alphaComponent = 0xff000000;
            }
            else if (input.Length != 8)
            {
                return false;
            }

            if (!uint.TryParse(input, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out uint parsed))
            {
                return false;
            }

            color = FromUInt32(parsed | alphaComponent);

            return true;
        }

        color = default;

        ReadOnlySpan<char> input = s.Slice(1);

        // Handle shorthand cases like #FFF (RGB) or #FFFF (ARGB).
        if (input.Length == 3 || input.Length == 4)
        {
            int extendedLength = 2 * input.Length;

            Span<char> extended = stackalloc char[extendedLength];
            for (int i = 0; i < input.Length; i++)
            {
                extended[2 * i + 0] = input[i];
                extended[2 * i + 1] = input[i];
            }

            return TryParseCore(extended, ref color);
        }

        return TryParseCore(input, ref color);
    }

    /// <summary>
    /// Returns the string representation of the color.
    /// </summary>
    /// <returns>
    /// The string representation of the color.
    /// </returns>
    public override string ToString()
    {
        uint rgb = ToUint32();
        return KnownColors.GetKnownColorName(rgb) ?? $"#{rgb:x8}";
    }

    /// <summary>
    /// Returns the integer representation of the color.
    /// </summary>
    /// <returns>
    /// The integer representation of the color.
    /// </returns>
    public uint ToUint32()
    {
        return (uint)A << 24 | (uint)R << 16 | (uint)G << 8 | B;
    }

    /// <summary>
    /// Returns the integer representation of the color.
    /// </summary>
    /// <returns>
    /// The integer representation of the color.
    /// </returns>
    public int ToInt32()
    {
        return A << 24 | R << 16 | G << 8 | B;
    }

    /// <summary>
    /// Converts this 32Bit color to HSV.
    /// </summary>
    /// <returns>Returns the HSV.</returns>
    public Hsv ToHsv()
    {
        float h = default;
        float s;
        float v;
        float a = A / 255F;
        float min = Math.Min(Math.Min(R, G), B);
        float max = Math.Max(Math.Max(R, G), B);

        float delta = max - min;

        v = 100.0F * max / 255.0F;

        if (max == 0.0F)
        {
            s = 0;
        }
        else
        {
            s = 100.0F * delta / max;
        }

        if (s == 0)
        {
            h = 0;
        }
        else
        {
            if (R == max)
            {
                h = 60.0F * (G - B) / delta;
            }
            else if (G == max)
            {
                h = 120.0F + (60.0F * (B - R) / delta);
            }
            else if (B == max)
            {
                h = 240.0F + (60.0F * (R - G) / delta);
            }

            if (h < 0.0)
            {
                h += 360.0F;
            }
        }

        return new Hsv(h, s, v, a);
    }

    /// <summary>
    /// Converts this 32Bit color to CMYK.
    /// </summary>
    /// <returns>Returns the CMYK.</returns>
    public Cmyk ToCmyk()
    {
        float rr = R / 255.0F;
        float gg = G / 255.0F;
        float bb = B / 255.0F;
        float aa = A / 255.0F;

        float k = 1.0F - Math.Max(Math.Max(rr, gg), bb);
        float c = (1.0F - rr - k) / (1.0F - k);
        float m = (1.0F - gg - k) / (1.0F - k);
        float y = (1.0F - bb - k) / (1.0F - k);

        return new Cmyk(c, m, y, k, aa);
    }

    /// <summary>
    /// Check if two colors are equal.
    /// </summary>
    public bool Equals(Color other)
    {
        return A == other.A && R == other.R && G == other.G && B == other.B;
    }

    public override bool Equals(object? obj)
    {
        return obj is Color other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(A, R, G, B);
    }

    public static bool operator ==(Color left, Color right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Color left, Color right)
    {
        return !left.Equals(right);
    }

    static Color IParsable<Color>.Parse(string s, IFormatProvider? provider)
    {
        return Parse(s);
    }

    static bool IParsable<Color>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Color result)
    {
        result = default;
        return s != null && TryParse(s, out result);
    }

    static Color ISpanParsable<Color>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        return Parse(s);
    }

    static bool ISpanParsable<Color>.TryParse([NotNullWhen(true)] ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out Color result)
    {
        return TryParse(s, out result);
    }
}
