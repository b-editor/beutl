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
[JsonConverter(typeof(ParsableJsonConverter<Color>))]
[TypeConverter(typeof(ParsableTypeConverter<Color>))]
public readonly struct Color(byte a, byte r, byte g, byte b)
    : IEquatable<Color>,
      IParsable<Color>,
      ISpanParsable<Color>
{

    public byte A { get; } = a;

    public byte R { get; } = r;

    public byte G { get; } = g;

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

    public static Color FromUInt32(uint value)
    {
        return new Color(
            (byte)(value >> 24 & 0xff),
            (byte)(value >> 16 & 0xff),
            (byte)(value >> 8 & 0xff),
            (byte)(value & 0xff)
        );
    }

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

    public static Color Parse(ReadOnlySpan<char> s)
    {
        if (TryParse(s, out Color color))
        {
            return color;
        }

        throw new FormatException($"Invalid color string: '{s.ToString()}'.");
    }

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

    public override string ToString()
    {
        uint rgb = ToUint32();
        return KnownColors.GetKnownColorName(rgb) ?? $"#{rgb:x8}";
    }

    public uint ToUint32()
    {
        return (uint)A << 24 | (uint)R << 16 | (uint)G << 8 | B;
    }

    public Vector4 ToLinear()
    {
        float a = A / 255f;
        float r = SrgbToLinear(R / 255f);
        float g = SrgbToLinear(G / 255f);
        float b = SrgbToLinear(B / 255f);

        return new Vector4(r, g, b, a);
    }

    public Vector4 ToLinearPremultiplied()
    {
        float a = A / 255f;
        float r = SrgbToLinear(R / 255f) * a;
        float g = SrgbToLinear(G / 255f) * a;
        float b = SrgbToLinear(B / 255f) * a;

        return new Vector4(r, g, b, a);
    }

    internal static float SrgbToLinear(float srgb)
    {
        return srgb <= 0.04045f ? srgb / 12.92f : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);
    }

    internal static float LinearToSrgb(float linear)
    {
        return linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1 / 2.4f) - 0.055f;
    }

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
        return TryParse(s!, out result);
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
