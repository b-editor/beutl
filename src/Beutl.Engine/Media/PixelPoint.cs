using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

using Beutl.Converters;
using Beutl.Graphics;
using Beutl.Utilities;
using Vector = Beutl.Graphics.Vector;

namespace Beutl.Media;

/// <summary>
/// Represents a point in device pixels.
/// </summary>
/// <param name="x">The X co-ordinate.</param>
/// <param name="y">The Y co-ordinate.</param>
[JsonConverter(typeof(ParsableJsonConverter<PixelPoint>))]
[TypeConverter(typeof(ParsableTypeConverter<PixelPoint>))]
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

    public int X { get; } = x;

    public int Y { get; } = y;

    static int ITupleConvertible<PixelPoint, int>.TupleLength => 2;

    public static bool operator ==(PixelPoint left, PixelPoint right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(PixelPoint left, PixelPoint right)
    {
        return !(left == right);
    }

    public static PixelPoint operator +(PixelPoint a, PixelPoint b)
    {
        return new PixelPoint(a.X + b.X, a.Y + b.Y);
    }

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
    public static bool TryParse(string? s, out PixelPoint rect)
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

    public bool Equals(PixelPoint other)
    {
        return X == other.X && Y == other.Y;
    }

    public override bool Equals(object? obj)
    {
        return obj is PixelPoint other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    public PixelPoint WithX(int x)
    {
        return new PixelPoint(x, Y);
    }

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
    /// Converts a <see cref="Point"/> to device pixels, truncating each co-ordinate toward zero.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>The device pixel point.</returns>
    /// <remarks>
    /// Truncation is not a floor: negative co-ordinates round up. Use <see cref="PixelRect.FromRect(Rect)"/>
    /// when a conservative device-pixel cover is required.
    /// </remarks>
    public static PixelPoint FromPoint(Point point)
    {
        return new PixelPoint((int)point.X, (int)point.Y);
    }

    /// <summary>
    /// Converts a <see cref="Point"/> to device pixels using the specified scaling factor, truncating each
    /// co-ordinate toward zero.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The device pixel point.</returns>
    /// <remarks>
    /// Truncation is not a floor: negative co-ordinates round up. Use
    /// <see cref="PixelRect.FromRect(Rect, float)"/> when a conservative device-pixel cover is required.
    /// </remarks>
    public static PixelPoint FromPoint(Point point, float scale)
    {
        return new PixelPoint(
            (int)(point.X * scale),
            (int)(point.Y * scale));
    }

    /// <summary>
    /// Converts a <see cref="Point"/> to device pixels using the specified scaling factor, truncating each
    /// co-ordinate toward zero.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The device pixel point.</returns>
    /// <remarks>
    /// Truncation is not a floor: negative co-ordinates round up. Use
    /// <see cref="PixelRect.FromRect(Rect, Vector)"/> when a conservative device-pixel cover is required.
    /// </remarks>
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
        return TryParse(s, out result);
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
