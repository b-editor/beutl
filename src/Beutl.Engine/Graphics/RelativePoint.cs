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
/// Defines a point that may be defined relative to a containing element.
/// </summary>
[JsonConverter(typeof(RelativePointJsonConverter))]
[TypeConverter(typeof(RelativePointConverter))]
public readonly struct RelativePoint(Point point, RelativeUnit unit)
    : IEquatable<RelativePoint>,
      IParsable<RelativePoint>,
      ISpanParsable<RelativePoint>,
      ISpanFormattable,
      IUtf8SpanParsable<RelativePoint>,
      IUtf8SpanFormattable,
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

    public RelativePoint(float x, float y, RelativeUnit unit)
        : this(new Point(x, y), unit)
    {
    }

    public Point Point { get; } = point;

    public RelativeUnit Unit { get; } = unit;

    public static bool operator ==(RelativePoint left, RelativePoint right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(RelativePoint left, RelativePoint right)
    {
        return !left.Equals(right);
    }

    public override bool Equals(object? obj) => obj is RelativePoint other && Equals(other);

    public bool Equals(RelativePoint p)
    {
        return Unit == p.Unit && Point == p.Point;
    }

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

    public static bool TryParse(string s, out RelativePoint point)
    {
        return TryParse(s.AsSpan(), null, out point);
    }

    public static bool TryParse(ReadOnlySpan<char> s, out RelativePoint point)
    {
        return TryParse(s, null, out point);
    }

    public static RelativePoint Parse(string s)
    {
        return Parse(s.AsSpan());
    }

    public static RelativePoint Parse(ReadOnlySpan<char> s)
    {
        return Parse(s, null);
    }

    public override string ToString()
    {
        return Unit == RelativeUnit.Absolute ?
            Point.ToString() :
            FormattableString.Invariant($"{Point.X * 100}%, {Point.Y * 100}%");
    }

    public static RelativePoint Parse(string s, IFormatProvider? provider)
    {
        return Parse(s.AsSpan(), provider);
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out RelativePoint result)
    {
        return TryParse(s.AsSpan(), provider, out result);
    }

    public static RelativePoint Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        provider ??= CultureInfo.InvariantCulture;
        using (var tokenizer = new RefStringTokenizer(s, provider, exceptionMessage: "Invalid RelativePoint."))
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
                float.Parse(x, provider: provider) * scale,
                float.Parse(y, provider: provider) * scale,
                unit);
        }
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out RelativePoint result)
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

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        if (Unit == RelativeUnit.Absolute)
        {
            return Point.TryFormat(destination, out charsWritten, default, provider);
        }
        else
        {
            char separator = TokenizerHelper.GetSeparatorFromFormatProvider(provider);
            return MemoryExtensions.TryWrite(destination, provider, $"{Point.X * 100}%{separator} {Point.Y * 100}%", out charsWritten);
        }
    }

    public string ToString(IFormatProvider? formatProvider)
    {
        if (Unit == RelativeUnit.Absolute)
        {
            return Point.ToString(formatProvider);
        }
        else
        {
            char separator = TokenizerHelper.GetSeparatorFromFormatProvider(formatProvider);
            return string.Create(formatProvider, $"{Point.X * 100}%{separator} {Point.Y * 100}%");
        }
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return ToString(formatProvider);
    }

    public static RelativePoint Parse(ReadOnlySpan<byte> utf8Text)
    {
        return Parse(utf8Text, null);
    }

    public static RelativePoint Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
    {
        return Utf8TextParser.Parse<RelativePoint>(utf8Text, provider);
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out RelativePoint result)
    {
        return TryParse(utf8Text, null, out result);
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out RelativePoint result)
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
        if (Unit == RelativeUnit.Absolute)
        {
            return Point.TryFormat(utf8Destination, out bytesWritten, default, provider);
        }
        else
        {
            char separator = TokenizerHelper.GetSeparatorFromFormatProvider(provider);
            return Utf8.TryWrite(utf8Destination, provider, $"{Point.X * 100}%{separator} {Point.Y * 100}%", out bytesWritten);
        }
    }
}
