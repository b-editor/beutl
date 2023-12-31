using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Text.Unicode;

using Beutl.Converters;
using Beutl.Utilities;
using Beutl.Validation;

namespace Beutl.Graphics;

/// <summary>
/// Defines a point.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="Point"/> structure.
/// </remarks>
/// <param name="x">The X position.</param>
/// <param name="y">The Y position.</param>
[JsonConverter(typeof(PointJsonConverter))]
[TypeConverter(typeof(PointConverter))]
public readonly struct Point(float x, float y)
    : IEquatable<Point>,
      IParsable<Point>,
      IFormattable,
      ISpanParsable<Point>,
      ISpanFormattable,
      IUtf8SpanParsable<Point>,
      IUtf8SpanFormattable,
      IEqualityOperators<Point, Point, bool>,
      IUnaryNegationOperators<Point, Point>,
      IAdditionOperators<Point, Point, Point>,
      IAdditionOperators<Point, Vector, Point>,
      ISubtractionOperators<Point, Point, Point>,
      ISubtractionOperators<Point, Vector, Point>,
      IMultiplyOperators<Point, float, Point>,
      IDivisionOperators<Point, float, Point>,
      IMultiplyOperators<Point, Matrix, Point>,
      ITupleConvertible<Point, float>
{

    /// <summary>
    /// Gets the X position.
    /// </summary>
    public float X { get; } = x;

    /// <summary>
    /// Gets the Y position.
    /// </summary>
    public float Y { get; } = y;

    /// <summary>
    /// Gets a value indicating whether the X and Y coordinates are zero.
    /// </summary>
    public bool IsDefault => (X == 0) && (Y == 0);

    static int ITupleConvertible<Point, float>.TupleLength => 2;

    /// <summary>
    /// Converts the <see cref="Point"/> to a <see cref="Vector"/>.
    /// </summary>
    /// <param name="p">The point.</param>
    public static implicit operator Vector(Point p)
    {
        return new Vector(p.X, p.Y);
    }

    /// <summary>
    /// Negates a point.
    /// </summary>
    /// <param name="a">The point.</param>
    /// <returns>The negated point.</returns>
    public static Point operator -(Point a)
    {
        return new Point(-a.X, -a.Y);
    }

    /// <summary>
    /// Checks for equality between two <see cref="Point"/>s.
    /// </summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>True if the points are equal; otherwise false.</returns>
    public static bool operator ==(Point left, Point right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Checks for inequality between two <see cref="Point"/>s.
    /// </summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>True if the points are unequal; otherwise false.</returns>
    public static bool operator !=(Point left, Point right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Adds two points.
    /// </summary>
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <returns>A point that is the result of the addition.</returns>
    public static Point operator +(Point a, Point b)
    {
        return new Point(a.X + b.X, a.Y + b.Y);
    }

    /// <summary>
    /// Adds a vector to a point.
    /// </summary>
    /// <param name="a">The point.</param>
    /// <param name="b">The vector.</param>
    /// <returns>A point that is the result of the addition.</returns>
    public static Point operator +(Point a, Vector b)
    {
        return new Point(a.X + b.X, a.Y + b.Y);
    }

    /// <summary>
    /// Subtracts two points.
    /// </summary>
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <returns>A point that is the result of the subtraction.</returns>
    public static Point operator -(Point a, Point b)
    {
        return new Point(a.X - b.X, a.Y - b.Y);
    }

    /// <summary>
    /// Subtracts a vector from a point.
    /// </summary>
    /// <param name="a">The point.</param>
    /// <param name="b">The vector.</param>
    /// <returns>A point that is the result of the subtraction.</returns>
    public static Point operator -(Point a, Vector b)
    {
        return new Point(a.X - b.X, a.Y - b.Y);
    }

    /// <summary>
    /// Multiplies a point by a factor coordinate-wise
    /// </summary>
    /// <param name="p">Point to multiply</param>
    /// <param name="k">Factor</param>
    /// <returns>Points having its coordinates multiplied</returns>
    public static Point operator *(Point p, float k)
    {
        return new Point(p.X * k, p.Y * k);
    }

    /// <summary>
    /// Multiplies a point by a factor coordinate-wise
    /// </summary>
    /// <param name="p">Point to multiply</param>
    /// <param name="k">Factor</param>
    /// <returns>Points having its coordinates multiplied</returns>
    public static Point operator *(float k, Point p)
    {
        return new Point(p.X * k, p.Y * k);
    }

    /// <summary>
    /// Divides a point by a factor coordinate-wise
    /// </summary>
    /// <param name="p">Point to divide by</param>
    /// <param name="k">Factor</param>
    /// <returns>Points having its coordinates divided</returns>
    public static Point operator /(Point p, float k)
    {
        return new Point(p.X / k, p.Y / k);
    }

    /// <summary>
    /// Applies a matrix to a point.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <param name="matrix">The matrix.</param>
    /// <returns>The resulting point.</returns>
    public static Point operator *(Point point, Matrix matrix) => matrix.Transform(point);

    /// <summary>
    /// Parses a <see cref="Point"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="point">The <see cref="Point"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(string s, out Point point)
    {
        return TryParse(s.AsSpan(), null, out point);
    }

    /// <summary>
    /// Parses a <see cref="Point"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="point">The <see cref="Point"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out Point point)
    {
        return TryParse(s, null, out point);
    }

    /// <summary>
    /// Parses a <see cref="Point"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The <see cref="Point"/>.</returns>
    public static Point Parse(string s)
    {
        return Parse(s.AsSpan(), null);
    }

    /// <summary>
    /// Parses a <see cref="Point"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The <see cref="Point"/>.</returns>
    public static Point Parse(ReadOnlySpan<char> s)
    {
        return Parse(s, null);
    }

    /// <summary>
    /// Returns a boolean indicating whether the point is equal to the other given point.
    /// </summary>
    /// <param name="other">The other point to test equality against.</param>
    /// <returns>True if this point is equal to other; False otherwise.</returns>
    public bool Equals(Point other)
    {
        return X == other.X &&
               Y == other.Y;
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
        return obj is Point other && Equals(other);
    }

    /// <summary>
    /// Returns a hash code for a <see cref="Point"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    /// <summary>
    /// Returns the string representation of the point.
    /// </summary>
    /// <returns>The string representation of the point.</returns>
    public override string ToString()
    {
        return FormattableString.Invariant($"{X}, {Y}");
    }

    /// <summary>
    /// Transforms the point by a matrix.
    /// </summary>
    /// <param name="transform">The transform.</param>
    /// <returns>The transformed point.</returns>
    public Point Transform(Matrix transform)
    {
        return transform.Transform(this);
    }

    /// <summary>
    /// Returns a new point with the specified X coordinate.
    /// </summary>
    /// <param name="x">The X coordinate.</param>
    /// <returns>The new point.</returns>
    public Point WithX(float x)
    {
        return new Point(x, Y);
    }

    /// <summary>
    /// Returns a new point with the specified Y coordinate.
    /// </summary>
    /// <param name="y">The Y coordinate.</param>
    /// <returns>The new point.</returns>
    public Point WithY(float y)
    {
        return new Point(X, y);
    }

    /// <summary>
    /// Deconstructs the point into its X and Y coordinates.
    /// </summary>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    public void Deconstruct(out float x, out float y)
    {
        x = X;
        y = Y;
    }

    public static Point Parse(string s, IFormatProvider? provider)
    {
        return Parse(s.AsSpan(), provider);
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Point result)
    {
        return TryParse(s.AsSpan(), provider, out result);
    }

    public static Point Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        using var tokenizer = new RefStringTokenizer(s, provider ?? CultureInfo.InvariantCulture, exceptionMessage: "Invalid Point.");
        return new Point(
            tokenizer.ReadSingle(),
            tokenizer.ReadSingle()
        );
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Point result)
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

    static void ITupleConvertible<Point, float>.ConvertTo(Point self, Span<float> tuple)
    {
        tuple[0] = self.X;
        tuple[1] = self.Y;
    }

    static void ITupleConvertible<Point, float>.ConvertFrom(Span<float> tuple, out Point self)
    {
        self = new Point(tuple[0], tuple[1]);
    }

    public string ToString(IFormatProvider? formatProvider)
    {
        if (formatProvider == null)
        {
            return ToString();
        }
        else
        {
            char separator = TokenizerHelper.GetSeparatorFromFormatProvider(formatProvider);
            return string.Create(formatProvider, $"{X}{separator} {Y}");
        }
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return ToString(formatProvider);
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        char separator = TokenizerHelper.GetSeparatorFromFormatProvider(provider);
        return MemoryExtensions.TryWrite(destination, provider, $"{X}{separator} {Y}", out charsWritten);
    }

    public static Point Parse(ReadOnlySpan<byte> utf8Text)
    {
        return Parse(utf8Text, null);
    }

    public static Point Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
    {
        using var tokenizer = new RefUtf8StringTokenizer(utf8Text, provider ?? CultureInfo.InvariantCulture, exceptionMessage: "Invalid Point.");
        return new Point(
            tokenizer.ReadSingle(),
            tokenizer.ReadSingle()
        );
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out Point result)
    {
        return TryParse(utf8Text, null, out result);
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out Point result)
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

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        char separator = TokenizerHelper.GetSeparatorFromFormatProvider(provider);
        return Utf8.TryWrite(utf8Destination, provider, $"{X}{separator} {Y}", out bytesWritten);
    }
}
