using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

using Beutl.Converters;
using Beutl.Utilities;
using Beutl.Validation;

namespace Beutl.Graphics;

/// <summary>
/// Defines a point that may be defined relative to a containing element.
/// </summary>
[JsonConverter(typeof(RelativePointJsonConverter))]
public readonly struct RelativePoint
    : IEquatable<RelativePoint>,
      IParsable<RelativePoint>,
      ISpanParsable<RelativePoint>,
      IEqualityOperators<RelativePoint, RelativePoint, bool>
{
    /// <summary>
    /// A point at the top left of the containing element.
    /// </summary>
    public static readonly RelativePoint TopLeft = new(0, 0, RelativeUnit.Relative);

    /// <summary>
    /// A point at the center of the containing element.
    /// </summary>
    public static readonly RelativePoint Center = new(0.5f, 0.5f, RelativeUnit.Relative);

    /// <summary>
    /// A point at the bottom right of the containing element.
    /// </summary>
    public static readonly RelativePoint BottomRight = new(1, 1, RelativeUnit.Relative);

    /// <summary>
    /// Initializes a new instance of the <see cref="RelativePoint"/> struct.
    /// </summary>
    /// <param name="x">The X point.</param>
    /// <param name="y">The Y point</param>
    /// <param name="unit">The unit.</param>
    public RelativePoint(float x, float y, RelativeUnit unit)
        : this(new Point(x, y), unit)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RelativePoint"/> struct.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <param name="unit">The unit.</param>
    public RelativePoint(Point point, RelativeUnit unit)
    {
        Point = point;
        Unit = unit;
    }

    /// <summary>
    /// Gets the point.
    /// </summary>
    public Point Point { get; }

    /// <summary>
    /// Gets the unit.
    /// </summary>
    public RelativeUnit Unit { get; }

    /// <summary>
    /// Checks for equality between two <see cref="RelativePoint"/>s.
    /// </summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>True if the points are equal; otherwise false.</returns>
    public static bool operator ==(RelativePoint left, RelativePoint right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Checks for inequality between two <see cref="RelativePoint"/>s.
    /// </summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>True if the points are unequal; otherwise false.</returns>
    public static bool operator !=(RelativePoint left, RelativePoint right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Checks if the <see cref="RelativePoint"/> equals another object.
    /// </summary>
    /// <param name="obj">The other object.</param>
    /// <returns>True if the objects are equal, otherwise false.</returns>
    public override bool Equals(object? obj) => obj is RelativePoint other && Equals(other);

    /// <summary>
    /// Checks if the <see cref="RelativePoint"/> equals another point.
    /// </summary>
    /// <param name="p">The other point.</param>
    /// <returns>True if the objects are equal, otherwise false.</returns>
    public bool Equals(RelativePoint p)
    {
        return Unit == p.Unit && Point == p.Point;
    }

    /// <summary>
    /// Gets a hashcode for a <see cref="RelativePoint"/>.
    /// </summary>
    /// <returns>A hash code.</returns>
    public override int GetHashCode()
    {
        unchecked
        {
            return (Point.GetHashCode() * 397) ^ (int)Unit;
        }
    }

    /// <summary>
    /// Converts a <see cref="RelativePoint"/> into pixels.
    /// </summary>
    /// <param name="size">The size of the visual.</param>
    /// <returns>The origin point in pixels.</returns>
    public Point ToPixels(Size size)
    {
        return Unit == RelativeUnit.Absolute ?
            Point :
            new Point(Point.X * size.Width, Point.Y * size.Height);
    }

    /// <summary>
    /// Parses a <see cref="RelativePoint"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="point">The <see cref="RelativePoint"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(string s, out RelativePoint point)
    {
        return TryParse(s.AsSpan(), out point);
    }

    /// <summary>
    /// Parses a <see cref="RelativePoint"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="point">The <see cref="RelativePoint"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out RelativePoint point)
    {
        try
        {
            point = Parse(s);
            return true;
        }
        catch
        {
            point = default;
            return false;
        }
    }

    /// <summary>
    /// Parses a <see cref="RelativePoint"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The parsed <see cref="RelativePoint"/>.</returns>
    public static RelativePoint Parse(string s)
    {
        return Parse(s.AsSpan());
    }

    /// <summary>
    /// Parses a <see cref="RelativePoint"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The parsed <see cref="RelativePoint"/>.</returns>
    public static RelativePoint Parse(ReadOnlySpan<char> s)
    {
        using (var tokenizer = new RefStringTokenizer(s, CultureInfo.InvariantCulture, exceptionMessage: "Invalid RelativePoint."))
        {
            ReadOnlySpan<char> x = tokenizer.ReadString();
            ReadOnlySpan<char> y = tokenizer.ReadString();

            RelativeUnit unit = RelativeUnit.Absolute;
            float scale = 1.0f;

            if (x.EndsWith("%", StringComparison.Ordinal))
            {
                if (!y.EndsWith("%", StringComparison.Ordinal))
                {
                    throw new FormatException("If one coordinate is relative, both must be.");
                }

                x = x[..^1];
                y = y[..^1];
                unit = RelativeUnit.Relative;
                scale = 0.01f;
            }

            return new RelativePoint(
                float.Parse(x, provider: CultureInfo.InvariantCulture) * scale,
                float.Parse(y, provider: CultureInfo.InvariantCulture) * scale,
                unit);
        }
    }

    /// <summary>
    /// Returns a String representing this RelativePoint instance.
    /// </summary>
    /// <returns>The string representation.</returns>
    public override string ToString()
    {
        return Unit == RelativeUnit.Absolute ?
            Point.ToString() :
            FormattableString.Invariant($"{Point.X * 100}%, {Point.Y * 100}%");
    }

    static RelativePoint IParsable<RelativePoint>.Parse(string s, IFormatProvider? provider)
    {
        return Parse(s);
    }

    static bool IParsable<RelativePoint>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out RelativePoint result)
    {
        result = default;
        return s != null && TryParse(s, out result);
    }

    static RelativePoint ISpanParsable<RelativePoint>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        return Parse(s);
    }

    static bool ISpanParsable<RelativePoint>.TryParse([NotNullWhen(true)] ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out RelativePoint result)
    {
        return TryParse(s, out result);
    }
}
