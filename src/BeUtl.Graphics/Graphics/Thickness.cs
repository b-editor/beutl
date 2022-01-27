using System.Globalization;
using System.Text.Json.Serialization;

using BeUtl.Converters;
using BeUtl.Utilities;

namespace BeUtl.Graphics;

/// <summary>
/// Describes the thickness of a frame around a rectangle.
/// </summary>
[JsonConverter(typeof(ThicknessJsonConverter))]
public readonly struct Thickness : IEquatable<Thickness>
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
    /// Adds a Thickness to a Size.
    /// </summary>
    /// <param name="size">The size.</param>
    /// <param name="thickness">The thickness.</param>
    /// <returns>The equality.</returns>
    public static Size operator +(Size size, Thickness thickness)
    {
        return new Size(
            size.Width + thickness.Left + thickness.Right,
            size.Height + thickness.Top + thickness.Bottom);
    }

    /// <summary>
    /// Subtracts a Thickness from a Size.
    /// </summary>
    /// <param name="size">The size.</param>
    /// <param name="thickness">The thickness.</param>
    /// <returns>The equality.</returns>
    public static Size operator -(Size size, Thickness thickness)
    {
        return new Size(
            size.Width - (thickness.Left + thickness.Right),
            size.Height - (thickness.Top + thickness.Bottom));
    }

    /// <summary>
    /// Parses a <see cref="Thickness"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="thickness">The <see cref="Thickness"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(string s, out Thickness thickness)
    {
        try
        {
            thickness = Parse(s);
            return true;
        }
        catch
        {
            thickness = default;
            return false;
        }
    }
    
    /// <summary>
    /// Parses a <see cref="Thickness"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="thickness">The <see cref="Thickness"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out Thickness thickness)
    {
        try
        {
            thickness = Parse(s);
            return true;
        }
        catch
        {
            thickness = default;
            return false;
        }
    }

    /// <summary>
    /// Parses a <see cref="Thickness"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The <see cref="Thickness"/>.</returns>
    public static Thickness Parse(string s)
    {
        const string exceptionMessage = "Invalid Thickness.";

        using var tokenizer = new StringTokenizer(s, CultureInfo.InvariantCulture, exceptionMessage);
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
    
    /// <summary>
    /// Parses a <see cref="Thickness"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The <see cref="Thickness"/>.</returns>
    public static Thickness Parse(ReadOnlySpan<char> s)
    {
        const string exceptionMessage = "Invalid Thickness.";

        using var tokenizer = new RefStringTokenizer(s, CultureInfo.InvariantCulture, exceptionMessage);
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
        return FormattableString.Invariant($"{Left},{Top},{Right},{Bottom}");
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
}
