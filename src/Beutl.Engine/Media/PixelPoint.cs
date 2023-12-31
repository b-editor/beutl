using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

using Beutl.Converters;
using Beutl.Graphics;
using Beutl.Utilities;
using Beutl.Validation;

using Vector = Beutl.Graphics.Vector;

namespace Beutl.Media;

/// <summary>
/// Represents a point in device pixels.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PixelPoint"/> structure.
/// </remarks>
/// <param name="x">The X co-ordinate.</param>
/// <param name="y">The Y co-ordinate.</param>
[JsonConverter(typeof(PixelPointJsonConverter))]
[TypeConverter(typeof(PixelPointConverter))]
public readonly struct PixelPoint(int x, int y)
    : IEquatable<PixelPoint>,
      IParsable<PixelPoint>,
      ISpanParsable<PixelPoint>,
      IEqualityOperators<PixelPoint, PixelPoint, bool>,
      IAdditionOperators<PixelPoint, PixelPoint, PixelPoint>,
      ISubtractionOperators<PixelPoint, PixelPoint, PixelPoint>,
      ITupleConvertible<PixelPoint, int>
{
    /// <summary>
    /// A point representing 0,0.
    /// </summary>
    public static readonly PixelPoint Origin = new(0, 0);

    /// <summary>
    /// Gets the X co-ordinate.
    /// </summary>
    public int X { get; } = x;

    /// <summary>
    /// Gets the Y co-ordinate.
    /// </summary>
    public int Y { get; } = y;

    static int ITupleConvertible<PixelPoint, int>.TupleLength => 2;

    /// <summary>
    /// Checks for equality between two <see cref="PixelPoint"/>s.
    /// </summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>True if the points are equal; otherwise false.</returns>
    public static bool operator ==(PixelPoint left, PixelPoint right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Checks for inequality between two <see cref="PixelPoint"/>s.
    /// </summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>True if the points are unequal; otherwise false.</returns>
    public static bool operator !=(PixelPoint left, PixelPoint right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Adds two points.
    /// </summary>
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <returns>A point that is the result of the addition.</returns>
    public static PixelPoint operator +(PixelPoint a, PixelPoint b)
    {
        return new PixelPoint(a.X + b.X, a.Y + b.Y);
    }

    /// <summary>
    /// Subtracts two points.
    /// </summary>
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <returns>A point that is the result of the subtraction.</returns>
    public static PixelPoint operator -(PixelPoint a, PixelPoint b)
    {
        return new PixelPoint(a.X - b.X, a.Y - b.Y);
    }

    /// <summary>
    /// Parses a <see cref="PixelPoint"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="rect">The <see cref="PixelPoint"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(string s, out PixelPoint rect)
    {
        return TryParse(s.AsSpan(), out rect);
    }

    /// <summary>
    /// Parses a <see cref="PixelPoint"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="rect">The <see cref="PixelPoint"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out PixelPoint rect)
    {
        try
        {
            rect = Parse(s);
            return true;
        }
        catch
        {
            rect = default;
            return false;
        }
    }

    /// <summary>
    /// Parses a <see cref="PixelPoint"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The <see cref="PixelPoint"/>.</returns>
    public static PixelPoint Parse(string s)
    {
        return Parse(s.AsSpan());
    }

    /// <summary>
    /// Parses a <see cref="PixelPoint"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The <see cref="PixelPoint"/>.</returns>
    public static PixelPoint Parse(ReadOnlySpan<char> s)
    {
        using var tokenizer = new RefStringTokenizer(s, CultureInfo.InvariantCulture, exceptionMessage: "Invalid PixelPoint.");
        return new PixelPoint(
            tokenizer.ReadInt32(),
            tokenizer.ReadInt32());
    }

    /// <summary>
    /// Returns a boolean indicating whether the point is equal to the other given point.
    /// </summary>
    /// <param name="other">The other point to test equality against.</param>
    /// <returns>True if this point is equal to other; False otherwise.</returns>
    public bool Equals(PixelPoint other)
    {
        return X == other.X && Y == other.Y;
    }

    /// <summary>
    /// Checks for equality between a point and an object.
    /// </summary>
    /// <param name="obj">The object.</param>
    /// <returns>
    /// True if <paramref name="obj"/> is a point that equals the current point.
    /// </returns>
    public override bool Equals(object? obj)
    {
        return obj is PixelPoint other && Equals(other);
    }

    /// <summary>
    /// Returns a hash code for a <see cref="PixelPoint"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    /// <summary>
    /// Returns a new <see cref="PixelPoint"/> with the same Y co-ordinate and the specified X co-ordinate.
    /// </summary>
    /// <param name="x">The X co-ordinate.</param>
    /// <returns>The new <see cref="PixelPoint"/>.</returns>
    public PixelPoint WithX(int x)
    {
        return new PixelPoint(x, Y);
    }

    /// <summary>
    /// Returns a new <see cref="PixelPoint"/> with the same X co-ordinate and the specified Y co-ordinate.
    /// </summary>
    /// <param name="y">The Y co-ordinate.</param>
    /// <returns>The new <see cref="PixelPoint"/>.</returns>
    public PixelPoint WithY(int y)
    {
        return new PixelPoint(X, y);
    }

    /// <summary>
    /// Converts the <see cref="PixelPoint"/> to a device-independent <see cref="Point"/> using the
    /// specified scaling factor.
    /// </summary>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The device-independent point.</returns>
    public Point ToPoint(float scale)
    {
        return new Point(X / scale, Y / scale);
    }

    /// <summary>
    /// Converts the <see cref="PixelPoint"/> to a device-independent <see cref="Point"/> using the
    /// specified scaling factor.
    /// </summary>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The device-independent point.</returns>
    public Point ToPoint(Vector scale)
    {
        return new Point(X / scale.X, Y / scale.Y);
    }

    /// <summary>
    /// Converts a <see cref="Point"/> to device pixels.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>The device-independent point.</returns>
    public static PixelPoint FromPoint(Point point)
    {
        return new PixelPoint((int)point.X, (int)point.Y);
    }

    /// <summary>
    /// Converts a <see cref="Point"/> to device pixels using the specified scaling factor.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The device-independent point.</returns>
    public static PixelPoint FromPoint(Point point, float scale)
    {
        return new PixelPoint(
            (int)(point.X * scale),
            (int)(point.Y * scale));
    }

    /// <summary>
    /// Converts a <see cref="Point"/> to device pixels using the specified scaling factor.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The device-independent point.</returns>
    public static PixelPoint FromPoint(Point point, Vector scale)
    {
        return new PixelPoint(
            (int)(point.X * scale.X),
            (int)(point.Y * scale.Y));
    }

    /// <summary>
    /// Returns the string representation of the point.
    /// </summary>
    /// <returns>The string representation of the point.</returns>
    public override string ToString()
    {
        return FormattableString.Invariant($"{X}, {Y}");
    }

    static PixelPoint IParsable<PixelPoint>.Parse(string s, IFormatProvider? provider)
    {
        return Parse(s);
    }

    static bool IParsable<PixelPoint>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out PixelPoint result)
    {
        result = default;
        return s != null && TryParse(s, out result);
    }

    static PixelPoint ISpanParsable<PixelPoint>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        return Parse(s);
    }

    static bool ISpanParsable<PixelPoint>.TryParse([NotNullWhen(true)] ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out PixelPoint result)
    {
        return TryParse(s, out result);
    }

    static void ITupleConvertible<PixelPoint, int>.ConvertTo(PixelPoint self, Span<int> tuple)
    {
        tuple[0] = self.X;
        tuple[1] = self.Y;
    }

    static void ITupleConvertible<PixelPoint, int>.ConvertFrom(Span<int> tuple, out PixelPoint self)
    {
        self = new PixelPoint(tuple[0], tuple[1]);
    }
}
