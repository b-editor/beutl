using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using Beutl.Converters;

namespace Beutl.Media;

/// <summary>
/// A color value used for color grading operations (Lift, Gamma, Gain, Offset).
/// Each component (R, G, B) is a float value that can be negative.
/// </summary>
[JsonConverter(typeof(GradingColorJsonConverter))]
[TypeConverter(typeof(GradingColorConverter))]
public readonly struct GradingColor
    : IEquatable<GradingColor>,
        IParsable<GradingColor>,
        ISpanParsable<GradingColor>
{
    /// <summary>
    /// Represents a neutral grading color (0, 0, 0) with intensity 1.
    /// </summary>
    public static readonly GradingColor Zero = new(0f, 0f, 0f);

    /// <summary>
    /// Represents a neutral grading color (1, 1, 1) with intensity 1.
    /// </summary>
    public static readonly GradingColor One = new(1f, 1f, 1f);

    /// <summary>
    /// Initializes a new instance of the <see cref="GradingColor"/> struct.
    /// </summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    public GradingColor(float r, float g, float b)
    {
        R = r;
        G = g;
        B = b;
    }

    /// <summary>
    /// Gets the Red component of the color.
    /// </summary>
    public float R { get; }

    /// <summary>
    /// Gets the Green component of the color.
    /// </summary>
    public float G { get; }

    /// <summary>
    /// Gets the Blue component of the color.
    /// </summary>
    public float B { get; }

    /// <summary>
    /// Creates a <see cref="GradingColor"/> from red, green and blue components.
    /// </summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <returns>The grading color.</returns>
    public static GradingColor FromRgb(float r, float g, float b)
    {
        return new GradingColor(r, g, b);
    }

    /// <summary>
    /// Creates a <see cref="GradingColor"/> from a <see cref="Color"/>.
    /// </summary>
    /// <param name="color">The source color.</param>
    /// <param name="intensity">The intensity multiplier (default: 1.0).</param>
    /// <param name="scale">The scale factor for conversion (default: 1/255).</param>
    /// <returns>The grading color.</returns>
    public static GradingColor FromColor(Color color, float scale = 1f / 255f)
    {
        return new GradingColor(
            color.R * scale,
            color.G * scale,
            color.B * scale);
    }

    /// <summary>
    /// Creates a <see cref="GradingColor"/> from a <see cref="Vector3"/>.
    /// </summary>
    /// <param name="vector">The source vector.</param>
    /// <returns>The grading color.</returns>
    public static GradingColor FromVector3(Vector3 vector)
    {
        return new GradingColor(vector.X, vector.Y, vector.Z);
    }

    /// <summary>
    /// Converts this <see cref="GradingColor"/> to a <see cref="Vector3"/>.
    /// Uses effective values (R * Intensity, G * Intensity, B * Intensity).
    /// </summary>
    /// <returns>The vector representation.</returns>
    public Vector3 ToVector3()
    {
        return new Vector3(R, G, B);
    }

    /// <summary>
    /// Converts this <see cref="GradingColor"/> to a <see cref="Color"/>.
    /// Uses effective values and clamps to 0-255 range.
    /// </summary>
    /// <returns>The color representation.</returns>
    public Color ToColor()
    {
        return Color.FromArgb(
            255,
            (byte)Math.Clamp(R * 255f, 0f, 255f),
            (byte)Math.Clamp(G * 255f, 0f, 255f),
            (byte)Math.Clamp(B * 255f, 0f, 255f));
    }

    /// <summary>
    /// Parses a grading color string in format "R, G, B" or "R, G, B, I".
    /// </summary>
    /// <param name="s">The color string.</param>
    /// <returns>The <see cref="GradingColor"/>.</returns>
    public static GradingColor Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);

        if (TryParse(s, out GradingColor color))
        {
            return color;
        }

        throw new FormatException($"Invalid grading color string: '{s}'.");
    }

    /// <summary>
    /// Parses a grading color string in format "R, G, B" or "R, G, B, I".
    /// </summary>
    /// <param name="s">The color string.</param>
    /// <returns>The <see cref="GradingColor"/>.</returns>
    public static GradingColor Parse(ReadOnlySpan<char> s)
    {
        if (TryParse(s, out GradingColor color))
        {
            return color;
        }

        throw new FormatException($"Invalid grading color string: '{s.ToString()}'.");
    }

    /// <summary>
    /// Tries to parse a grading color string in format "R, G, B" or "R, G, B, I".
    /// </summary>
    /// <param name="s">The color string.</param>
    /// <param name="color">The parsed color.</param>
    /// <returns>True if parsing was successful.</returns>
    public static bool TryParse(string? s, out GradingColor color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        return TryParse(s.AsSpan(), out color);
    }

    /// <summary>
    /// Tries to parse a grading color string in format "R, G, B" or "R, G, B, I".
    /// </summary>
    /// <param name="s">The color string.</param>
    /// <param name="color">The parsed color.</param>
    /// <returns>True if parsing was successful.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out GradingColor color)
    {
        color = default;
        s = s.Trim();

        if (s.Length == 0)
        {
            return false;
        }

        int firstComma = s.IndexOf(',');
        if (firstComma < 0)
        {
            return false;
        }

        int secondComma = s.Slice(firstComma + 1).IndexOf(',');
        if (secondComma < 0)
        {
            return false;
        }

        secondComma += firstComma + 1;

        ReadOnlySpan<char> rStr = s.Slice(0, firstComma).Trim();
        ReadOnlySpan<char> gStr = s.Slice(firstComma + 1, secondComma - firstComma - 1).Trim();
        ReadOnlySpan<char> bStr = s.Slice(secondComma + 1).Trim();

        if (!float.TryParse(rStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ||
            !float.TryParse(gStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float g) ||
            !float.TryParse(bStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float b))
        {
            return false;
        }

        color = new GradingColor(r, g, b);
        return true;
    }

    /// <summary>
    /// Returns the string representation of the grading color.
    /// Format: "R, G, B, I" if intensity is not 1.0, otherwise "R, G, B".
    /// </summary>
    /// <returns>The string representation.</returns>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{R}, {G}, {B}");
    }

    /// <inheritdoc/>
    public bool Equals(GradingColor other)
    {
        return R == other.R && G == other.G && B == other.B;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is GradingColor other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(R, G, B);
    }

    /// <summary>
    /// Checks if two grading colors are equal.
    /// </summary>
    public static bool operator ==(GradingColor left, GradingColor right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Checks if two grading colors are not equal.
    /// </summary>
    public static bool operator !=(GradingColor left, GradingColor right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Adds two grading colors. Intensity is averaged.
    /// </summary>
    public static GradingColor operator +(GradingColor left, GradingColor right)
    {
        return new GradingColor(
            left.R + right.R,
            left.G + right.G,
            left.B + right.B);
    }

    /// <summary>
    /// Subtracts two grading colors. Intensity is averaged.
    /// </summary>
    public static GradingColor operator -(GradingColor left, GradingColor right)
    {
        return new GradingColor(
            left.R - right.R,
            left.G - right.G,
            left.B - right.B);
    }

    /// <summary>
    /// Multiplies a grading color by a scalar (affects RGB, not intensity).
    /// </summary>
    public static GradingColor operator *(GradingColor color, float scalar)
    {
        return new GradingColor(color.R * scalar, color.G * scalar, color.B * scalar);
    }

    /// <summary>
    /// Multiplies a scalar by a grading color (affects RGB, not intensity).
    /// </summary>
    public static GradingColor operator *(float scalar, GradingColor color)
    {
        return color * scalar;
    }

    static GradingColor IParsable<GradingColor>.Parse(string s, IFormatProvider? provider)
    {
        return Parse(s);
    }

    static bool IParsable<GradingColor>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider,
        [MaybeNullWhen(false)] out GradingColor result)
    {
        return TryParse(s, out result);
    }

    static GradingColor ISpanParsable<GradingColor>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        return Parse(s);
    }

    static bool ISpanParsable<GradingColor>.TryParse([NotNullWhen(true)] ReadOnlySpan<char> s,
        IFormatProvider? provider, [MaybeNullWhen(false)] out GradingColor result)
    {
        return TryParse(s, out result);
    }
}
