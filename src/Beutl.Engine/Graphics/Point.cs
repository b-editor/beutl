using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Text.Unicode;

using Beutl.Converters;
using Beutl.Utilities;

namespace Beutl.Graphics;

[JsonConverter(typeof(ParsableJsonConverter<Point>))]
[TypeConverter(typeof(ParsableTypeConverter<Point>))]
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
    public static readonly Point Invalid = new(float.NaN, float.NaN);

    public float X { get; } = x;

    public float Y { get; } = y;

    /// <summary>
    /// Gets a value indicating whether the X and Y coordinates are zero.
    /// </summary>
    public bool IsDefault => (X == 0) && (Y == 0);

    /// <summary>
    /// Gets the value of whether any of the properties are NaN.
    /// </summary>
    public bool IsInvalid => float.IsNaN(X) || float.IsNaN(Y);

    static int ITupleConvertible<Point, float>.TupleLength => 2;

    public static implicit operator Vector(Point p)
    {
        return new Vector(p.X, p.Y);
    }

    public static Point operator -(Point a)
    {
        return new Point(-a.X, -a.Y);
    }

    public static bool operator ==(Point left, Point right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Point left, Point right)
    {
        return !(left == right);
    }

    public static Point operator +(Point a, Point b)
    {
        return new Point(a.X + b.X, a.Y + b.Y);
    }

    public static Point operator +(Point a, Vector b)
    {
        return new Point(a.X + b.X, a.Y + b.Y);
    }

    public static Point operator -(Point a, Point b)
    {
        return new Point(a.X - b.X, a.Y - b.Y);
    }

    public static Point operator -(Point a, Vector b)
    {
        return new Point(a.X - b.X, a.Y - b.Y);
    }

    public static Point operator *(Point p, float k)
    {
        return new Point(p.X * k, p.Y * k);
    }

    public static Point operator *(float k, Point p)
    {
        return new Point(p.X * k, p.Y * k);
    }

    public static Point operator /(Point p, float k)
    {
        return new Point(p.X / k, p.Y / k);
    }

    public static Point operator *(Point point, Matrix matrix) => matrix.Transform(point);

    public static bool TryParse(string? s, out Point point)
    {
        return TryParse(s.AsSpan(), null, out point);
    }

    public static bool TryParse(ReadOnlySpan<char> s, out Point point)
    {
        return TryParse(s, null, out point);
    }

    public static Point Parse(string s)
    {
        return Parse(s.AsSpan(), null);
    }

    public static Point Parse(ReadOnlySpan<char> s)
    {
        return Parse(s, null);
    }

    public bool Equals(Point other)
    {
        return X == other.X &&
               Y == other.Y;
    }

    public override bool Equals(object? obj)
    {
        return obj is Point other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    public override string ToString()
    {
        return FormattableString.Invariant($"{X}, {Y}");
    }

    public Point Transform(Matrix transform)
    {
        return transform.Transform(this);
    }

    public Point WithX(float x)
    {
        return new Point(x, Y);
    }

    public Point WithY(float y)
    {
        return new Point(X, y);
    }

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
        return Utf8TextParser.Parse<Point>(utf8Text, provider);
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
