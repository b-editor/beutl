using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Text.Unicode;

using Beutl.Converters;
using Beutl.Utilities;

namespace Beutl.Graphics;

/// <summary>
/// Describes the thickness of a frame around a rectangle.
/// </summary>
[JsonConverter(typeof(ThicknessJsonConverter))]
[TypeConverter(typeof(ThicknessConverter))]
public readonly struct Thickness
    : IEquatable<Thickness>,
      IParsable<Thickness>,
      ISpanParsable<Thickness>,
      ISpanFormattable,
      IUtf8SpanFormattable,
      IUtf8SpanParsable<Thickness>,
      IEqualityOperators<Thickness, Thickness, bool>,
      IAdditionOperators<Thickness, Thickness, Thickness>,
      ISubtractionOperators<Thickness, Thickness, Thickness>,
      IMultiplyOperators<Thickness, float, Thickness>,
      ITupleConvertible<Thickness, float>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Thickness"/> structure.
    /// </summary>
    /// <param name="uniformLength">The length that should be applied to all sides.</param>
    public Thickness(float uniformLength)
    {
        Left = Top = Right = Bottom = uniformLength;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Thickness"/> structure.
    /// </summary>
    /// <param name="horizontal">The thickness on the left and right.</param>
    /// <param name="vertical">The thickness on the top and bottom.</param>
    public Thickness(float horizontal, float vertical)
    {
        Left = Right = horizontal;
        Top = Bottom = vertical;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Thickness"/> structure.
    /// </summary>
    /// <param name="left">The thickness on the left.</param>
    /// <param name="top">The thickness on the top.</param>
    /// <param name="right">The thickness on the right.</param>
    /// <param name="bottom">The thickness on the bottom.</param>
    public Thickness(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    /// <summary>
    /// Gets the thickness on the left.
    /// </summary>
    public float Left { get; }

    /// <summary>
    /// Gets the thickness on the top.
    /// </summary>
    public float Top { get; }

    /// <summary>
    /// Gets the thickness on the right.
    /// </summary>
    public float Right { get; }

    /// <summary>
    /// Gets the thickness on the bottom.
    /// </summary>
    public float Bottom { get; }

    /// <summary>
    /// Gets a value indicating whether all sides are set to 0.
    /// </summary>
    public bool IsEmpty => Left.Equals(0) && IsUniform;

    /// <summary>
    /// Gets a value indicating whether all sides are equal.
    /// </summary>
    public bool IsUniform => Left.Equals(Right) && Top.Equals(Bottom) && Right.Equals(Bottom);

    /// <summary>
    /// Gets a value indicating whether the left, top, right and bottom thickness values are zero.
    /// </summary>
    public bool IsDefault => (Left == 0) && (Top == 0) && (Right == 0) && (Bottom == 0);

    static int ITupleConvertible<Thickness, float>.TupleLength => 4;

    /// <summary>
    /// Compares two Thicknesses.
    /// </summary>
    /// <param name="a">The first thickness.</param>
    /// <param name="b">The second thickness.</param>
    /// <returns>The equality.</returns>
    public static bool operator ==(Thickness a, Thickness b)
    {
        return a.Equals(b);
    }

    /// <summary>
    /// Compares two Thicknesses.
    /// </summary>
    /// <param name="a">The first thickness.</param>
    /// <param name="b">The second thickness.</param>
    /// <returns>The inequality.</returns>
    public static bool operator !=(Thickness a, Thickness b)
    {
        return !a.Equals(b);
    }

    /// <summary>
    /// Adds two Thicknesses.
    /// </summary>
    /// <param name="a">The first thickness.</param>
    /// <param name="b">The second thickness.</param>
    /// <returns>The equality.</returns>
    public static Thickness operator +(Thickness a, Thickness b)
    {
        return new Thickness(
            a.Left + b.Left,
            a.Top + b.Top,
            a.Right + b.Right,
            a.Bottom + b.Bottom);
    }

    /// <summary>
    /// Subtracts two Thicknesses.
    /// </summary>
    /// <param name="a">The first thickness.</param>
    /// <param name="b">The second thickness.</param>
    /// <returns>The equality.</returns>
    public static Thickness operator -(Thickness a, Thickness b)
    {
        return new Thickness(
            a.Left - b.Left,
            a.Top - b.Top,
            a.Right - b.Right,
            a.Bottom - b.Bottom);
    }

    /// <summary>
    /// Multiplies a Thickness to a scalar.
    /// </summary>
    /// <param name="a">The thickness.</param>
    /// <param name="b">The scalar.</param>
    /// <returns>The equality.</returns>
    public static Thickness operator *(Thickness a, float b)
    {
        return new Thickness(
            a.Left * b,
            a.Top * b,
            a.Right * b,
            a.Bottom * b);
    }

    /// <summary>
    /// Parses a <see cref="Thickness"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="thickness">The <see cref="Thickness"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(string s, out Thickness thickness)
    {
        return TryParse(s.AsSpan(), out thickness);
    }

    /// <summary>
    /// Parses a <see cref="Thickness"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="thickness">The <see cref="Thickness"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out Thickness thickness)
    {
        return TryParse(s, null, out thickness);
    }

    /// <summary>
    /// Parses a <see cref="Thickness"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The <see cref="Thickness"/>.</returns>
    public static Thickness Parse(string s)
    {
        return Parse(s.AsSpan());
    }

    /// <summary>
    /// Parses a <see cref="Thickness"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The <see cref="Thickness"/>.</returns>
    public static Thickness Parse(ReadOnlySpan<char> s)
    {
        return Parse(s, null);
    }

    /// <summary>
    /// Returns a boolean indicating whether the thickness is equal to the other given point.
    /// </summary>
    /// <param name="other">The other thickness to test equality against.</param>
    /// <returns>True if this thickness is equal to other; False otherwise.</returns>
    public bool Equals(Thickness other)
    {
        return Left == other.Left &&
               Top == other.Top &&
               Right == other.Right &&
               Bottom == other.Bottom;
    }

    /// <summary>
    /// Checks for equality between a thickness and an object.
    /// </summary>
    /// <param name="obj">The object.</param>
    /// <returns>
    /// True if <paramref name="obj"/> is a size that equals the current size.
    /// </returns>
    public override bool Equals(object? obj)
    {
        return obj is Thickness other && Equals(other);
    }

    /// <summary>
    /// Returns a hash code for a <see cref="Thickness"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Left, Top, Right, Bottom);
    }

    /// <summary>
    /// Returns the string representation of the thickness.
    /// </summary>
    /// <returns>The string representation of the thickness.</returns>
    public override string ToString()
    {
        if (Left == Right && Top == Bottom)
        {
            if (Left == Top)
            {
                return FormattableString.Invariant($"{Left}");
            }

            return FormattableString.Invariant($"{Left}, {Top}");
        }

        return FormattableString.Invariant($"{Left}, {Top}, {Right}, {Bottom}");
    }

    /// <summary>
    /// Returns a new <see cref="Thickness"/> with the specified left.
    /// </summary>
    /// <param name="left">The left.</param>
    /// <returns>The new <see cref="Thickness"/>.</returns>
    public Thickness WithLeft(float left)
    {
        return new Thickness(left, Top, Right, Bottom);
    }

    /// <summary>
    /// Returns a new <see cref="Thickness"/> with the specified top.
    /// </summary>
    /// <param name="top">The top.</param>
    /// <returns>The new <see cref="Thickness"/>.</returns>
    public Thickness WithTop(float top)
    {
        return new Thickness(Left, top, Right, Bottom);
    }

    /// <summary>
    /// Returns a new <see cref="Thickness"/> with the specified right.
    /// </summary>
    /// <param name="right">The right.</param>
    /// <returns>The new <see cref="Thickness"/>.</returns>
    public Thickness WithRight(float right)
    {
        return new Thickness(Left, Top, right, Bottom);
    }

    /// <summary>
    /// Returns a new <see cref="Thickness"/> with the specified bottom.
    /// </summary>
    /// <param name="bottom">The bottom.</param>
    /// <returns>The new <see cref="Thickness"/>.</returns>
    public Thickness WithBottom(float bottom)
    {
        return new Thickness(Left, Top, Right, bottom);
    }

    /// <summary>
    /// Deconstructor the thickness into its left, top, right and bottom thickness values.
    /// </summary>
    /// <param name="left">The thickness on the left.</param>
    /// <param name="top">The thickness on the top.</param>
    /// <param name="right">The thickness on the right.</param>
    /// <param name="bottom">The thickness on the bottom.</param>
    public void Deconstruct(out float left, out float top, out float right, out float bottom)
    {
        left = Left;
        top = Top;
        right = Right;
        bottom = Bottom;
    }

    public static Thickness Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        const string exceptionMessage = "Invalid Thickness.";

        using var tokenizer = new RefStringTokenizer(s, provider ?? CultureInfo.InvariantCulture, exceptionMessage);
        if (tokenizer.TryReadSingle(out float a))
        {
            if (tokenizer.TryReadSingle(out float b))
            {
                if (tokenizer.TryReadSingle(out float c))
                {
                    return new Thickness(a, b, c, tokenizer.ReadSingle());
                }

                return new Thickness(a, b);
            }

            return new Thickness(a);
        }

        throw new FormatException(exceptionMessage);
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Thickness result)
    {
        try
        {
            result = Parse(s, provider);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    static void ITupleConvertible<Thickness, float>.ConvertTo(Thickness self, Span<float> tuple)
    {
        tuple[0] = self.Left;
        tuple[1] = self.Top;
        tuple[2] = self.Right;
        tuple[3] = self.Bottom;
    }

    static void ITupleConvertible<Thickness, float>.ConvertFrom(Span<float> tuple, out Thickness self)
    {
        self = new Thickness(tuple[0], tuple[1], tuple[2], tuple[3]);
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        char separator = TokenizerHelper.GetSeparatorFromFormatProvider(provider);
        if (Left == Right && Top == Bottom)
        {
            if (Left == Top)
            {
                return Left.TryFormat(destination, out charsWritten, format, provider);
            }

            return MemoryExtensions.TryWrite(destination, provider, $"{Left}{separator} {Top}", out charsWritten);
        }

        return MemoryExtensions.TryWrite(destination, provider, $"{Left}{separator} {Top}{separator} {Right}{separator} {Bottom}", out charsWritten);
    }

    public string ToString(IFormatProvider? formatProvider)
    {
        return ToString(null, formatProvider);
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        if (formatProvider == null)
        {
            return ToString();
        }
        else
        {
            char separator = TokenizerHelper.GetSeparatorFromFormatProvider(formatProvider);

            if (Left == Right && Top == Bottom)
            {
                if (Left == Top)
                {
                    return Left.ToString(formatProvider);
                }

                return string.Create(formatProvider, $"{Left}{separator} {Top}");
            }

            return string.Create(formatProvider, $"{Left}{separator} {Top}{separator} {Right}{separator} {Bottom}");
        }
    }

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        char separator = TokenizerHelper.GetSeparatorFromFormatProvider(provider);
        if (Left == Right && Top == Bottom)
        {
            if (Left == Top)
            {
                return Left.TryFormat(utf8Destination, out bytesWritten, format, provider);
            }

            return Utf8.TryWrite(utf8Destination, provider, $"{Left}{separator} {Top}", out bytesWritten);
        }

        return Utf8.TryWrite(utf8Destination, provider, $"{Left}{separator} {Top}{separator} {Right}{separator} {Bottom}", out bytesWritten);
    }

    public static Thickness Parse(ReadOnlySpan<byte> utf8Text)
    {
        return Parse(utf8Text, null);
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out Thickness result)
    {
        return TryParse(utf8Text, null, out result);
    }

    public static Thickness Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
    {
        const string exceptionMessage = "Invalid Thickness.";

        using var tokenizer = new RefUtf8StringTokenizer(utf8Text, provider ?? CultureInfo.InvariantCulture, exceptionMessage);
        if (tokenizer.TryReadSingle(out float a))
        {
            if (tokenizer.TryReadSingle(out float b))
            {
                if (tokenizer.TryReadSingle(out float c))
                {
                    return new Thickness(a, b, c, tokenizer.ReadSingle());
                }

                return new Thickness(a, b);
            }

            return new Thickness(a);
        }

        throw new FormatException(exceptionMessage);
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out Thickness result)
    {
        try
        {
            result = Parse(utf8Text, provider);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    public static Thickness Parse(string s, IFormatProvider? provider)
    {
        return Parse(s.AsSpan(), provider);
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Thickness result)
    {
        return TryParse(s.AsSpan(), provider, out result);
    }
}
