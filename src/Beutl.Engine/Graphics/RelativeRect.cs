using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Unicode;

using Beutl.Utilities;

namespace Beutl.Graphics;

/// <summary>
/// Defines a rectangle that may be defined relative to a containing element.
/// </summary>
public readonly struct RelativeRect
    : IEquatable<RelativeRect>,
      IParsable<RelativeRect>,
      ISpanParsable<RelativeRect>,
      ISpanFormattable,
      IUtf8SpanParsable<RelativeRect>,
      IUtf8SpanFormattable,
      IEqualityOperators<RelativeRect, RelativeRect, bool>
{
    private static readonly char[] s_percentChar = ['%'];

    /// <summary>
    /// A rectangle that represents 100% of an area.
    /// </summary>
    public static readonly RelativeRect Fill = new(0, 0, 1, 1, RelativeUnit.Relative);

    /// <summary>
    /// Initializes a new instance of the <see cref="RelativeRect"/> structure.
    /// </summary>
    /// <param name="x">The X position.</param>
    /// <param name="y">The Y position.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="unit">The unit of the rect.</param>
    public RelativeRect(float x, float y, float width, float height, RelativeUnit unit)
    {
        Rect = new Rect(x, y, width, height);
        Unit = unit;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RelativeRect"/> structure.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="unit">The unit of the rect.</param>
    public RelativeRect(Rect rect, RelativeUnit unit)
    {
        Rect = rect;
        Unit = unit;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RelativeRect"/> structure.
    /// </summary>
    /// <param name="size">The size of the rectangle.</param>
    /// <param name="unit">The unit of the rect.</param>
    public RelativeRect(Size size, RelativeUnit unit)
    {
        Rect = new Rect(size);
        Unit = unit;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RelativeRect"/> structure.
    /// </summary>
    /// <param name="position">The position of the rectangle.</param>
    /// <param name="size">The size of the rectangle.</param>
    /// <param name="unit">The unit of the rect.</param>
    public RelativeRect(Point position, Size size, RelativeUnit unit)
    {
        Rect = new Rect(position, size);
        Unit = unit;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RelativeRect"/> structure.
    /// </summary>
    /// <param name="topLeft">The top left position of the rectangle.</param>
    /// <param name="bottomRight">The bottom right position of the rectangle.</param>
    /// <param name="unit">The unit of the rect.</param>
    public RelativeRect(Point topLeft, Point bottomRight, RelativeUnit unit)
    {
        Rect = new Rect(topLeft, bottomRight);
        Unit = unit;
    }

    /// <summary>
    /// Gets the unit of the rectangle.
    /// </summary>
    public RelativeUnit Unit { get; }

    /// <summary>
    /// Gets the rectangle.
    /// </summary>
    public Rect Rect { get; }

    /// <summary>
    /// Checks for equality between two <see cref="RelativeRect"/>s.
    /// </summary>
    /// <param name="left">The first rectangle.</param>
    /// <param name="right">The second rectangle.</param>
    /// <returns>True if the rectangles are equal; otherwise false.</returns>
    public static bool operator ==(RelativeRect left, RelativeRect right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Checks for inequality between two <see cref="RelativeRect"/>s.
    /// </summary>
    /// <param name="left">The first rectangle.</param>
    /// <param name="right">The second rectangle.</param>
    /// <returns>True if the rectangles are unequal; otherwise false.</returns>
    public static bool operator !=(RelativeRect left, RelativeRect right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Checks if the <see cref="RelativeRect"/> equals another object.
    /// </summary>
    /// <param name="obj">The other object.</param>
    /// <returns>True if the objects are equal, otherwise false.</returns>
    public override bool Equals(object? obj) => obj is RelativeRect other && Equals(other);

    /// <summary>
    /// Checks if the <see cref="RelativeRect"/> equals another rectangle.
    /// </summary>
    /// <param name="p">The other rectangle.</param>
    /// <returns>True if the objects are equal, otherwise false.</returns>
    public bool Equals(RelativeRect p)
    {
        return Unit == p.Unit && Rect == p.Rect;
    }

    /// <summary>
    /// Gets a hashcode for a <see cref="RelativeRect"/>.
    /// </summary>
    /// <returns>A hash code.</returns>
    public override int GetHashCode()
    {
        unchecked
        {
            return ((int)Unit * 397) ^ Rect.GetHashCode();
        }
    }

    /// <summary>
    /// Converts a <see cref="RelativeRect"/> into pixels.
    /// </summary>
    /// <param name="size">The size of the visual.</param>
    /// <returns>The origin point in pixels.</returns>
    public Rect ToPixels(Size size)
    {
        return Unit == RelativeUnit.Absolute ?
            Rect :
            new Rect(
                Rect.X * size.Width,
                Rect.Y * size.Height,
                Rect.Width * size.Width,
                Rect.Height * size.Height);
    }

    /// <summary>
    /// Parses a <see cref="RelativeRect"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="rect">The <see cref="RelativeRect"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(string s, out RelativeRect rect)
    {
        return TryParse(s.AsSpan(), out rect);
    }

    /// <summary>
    /// Parses a <see cref="RelativeRect"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="rect">The <see cref="RelativeRect"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out RelativeRect rect)
    {
        return TryParse(s, null, out rect);
    }

    /// <summary>
    /// Parses a <see cref="RelativeRect"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The parsed <see cref="RelativeRect"/>.</returns>
    public static RelativeRect Parse(string s)
    {
        return Parse(s.AsSpan());
    }

    /// <summary>
    /// Parses a <see cref="RelativeRect"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The parsed <see cref="RelativeRect"/>.</returns>
    public static RelativeRect Parse(ReadOnlySpan<char> s)
    {
        return Parse(s, null);
    }

    /// <summary>
    /// Returns a String representing this RelativeRect instance.
    /// </summary>
    /// <returns>The string representation.</returns>
    public override string ToString()
    {
        return Unit == RelativeUnit.Absolute ?
            Rect.ToString() :
            FormattableString.Invariant($"{Rect.X * 100}%, {Rect.Y * 100}%, {Rect.Width * 100}%, {Rect.Height * 100}%");
    }

    public static RelativeRect Parse(string s, IFormatProvider? provider)
    {
        return Parse(s.AsSpan(), provider);
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out RelativeRect result)
    {
        return TryParse(s.AsSpan(), provider, out result);
    }

    public static RelativeRect Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        provider ??= CultureInfo.InvariantCulture;
        using (var tokenizer = new RefStringTokenizer(s, provider, "Invalid RelativeRect."))
        {
            ReadOnlySpan<char> x = tokenizer.ReadString();
            ReadOnlySpan<char> y = tokenizer.ReadString();
            ReadOnlySpan<char> width = tokenizer.ReadString();
            ReadOnlySpan<char> height = tokenizer.ReadString();

            RelativeUnit unit = RelativeUnit.Absolute;
            float scale = 1.0f;

            bool xRelative = x.EndsWith("%", StringComparison.Ordinal);
            bool yRelative = y.EndsWith("%", StringComparison.Ordinal);
            bool widthRelative = width.EndsWith("%", StringComparison.Ordinal);
            bool heightRelative = height.EndsWith("%", StringComparison.Ordinal);

            if (xRelative && yRelative && widthRelative && heightRelative)
            {
                x = x.TrimEnd(s_percentChar);
                y = y.TrimEnd(s_percentChar);
                width = width.TrimEnd(s_percentChar);
                height = height.TrimEnd(s_percentChar);

                unit = RelativeUnit.Relative;
                scale = 0.01f;
            }
            else if (xRelative || yRelative || widthRelative || heightRelative)
            {
                throw new FormatException("If one coordinate is relative, all must be.");
            }

            return new RelativeRect(
                float.Parse(x, provider: provider) * scale,
                float.Parse(y, provider: provider) * scale,
                float.Parse(width, provider: provider) * scale,
                float.Parse(height, provider: provider) * scale,
                unit);
        }
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out RelativeRect result)
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

    public string ToString(IFormatProvider? formatProvider)
    {
        if (Unit == RelativeUnit.Absolute)
        {
            return Rect.ToString(formatProvider);
        }
        else
        {
            char separator = TokenizerHelper.GetSeparatorFromFormatProvider(formatProvider);
            return string.Create(
                formatProvider,
                $"{Rect.X * 100}%{separator} {Rect.Y * 100}%{separator} {Rect.Width * 100}%{separator} {Rect.Height * 100}%");
        }
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        if (Unit == RelativeUnit.Absolute)
        {
            return Rect.TryFormat(destination, out charsWritten, default, provider);
        }
        else
        {
            char separator = TokenizerHelper.GetSeparatorFromFormatProvider(provider);
            return MemoryExtensions.TryWrite(
                destination,
                provider,
                $"{Rect.X * 100}%{separator} {Rect.Y * 100}%{separator} {Rect.Width * 100}%{separator} {Rect.Height * 100}%",
                out charsWritten);
        }
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return ToString(formatProvider);
    }

    public static RelativeRect Parse(ReadOnlySpan<byte> utf8Text)
    {
        return Parse(utf8Text, null);
    }

    public static RelativeRect Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
    {
        provider ??= CultureInfo.InvariantCulture;
        using (var tokenizer = new RefUtf8StringTokenizer(utf8Text, provider, "Invalid RelativeRect."))
        {
            ReadOnlySpan<byte> x = tokenizer.ReadString();
            ReadOnlySpan<byte> y = tokenizer.ReadString();
            ReadOnlySpan<byte> width = tokenizer.ReadString();
            ReadOnlySpan<byte> height = tokenizer.ReadString();

            RelativeUnit unit = RelativeUnit.Absolute;
            float scale = 1.0f;

            ReadOnlySpan<byte> percentChar = "%"u8;
            bool xRelative = x.EndsWith(percentChar);
            bool yRelative = y.EndsWith(percentChar);
            bool widthRelative = width.EndsWith(percentChar);
            bool heightRelative = height.EndsWith(percentChar);

            if (xRelative && yRelative && widthRelative && heightRelative)
            {
                x = x.TrimEnd(percentChar);
                y = y.TrimEnd(percentChar);
                width = width.TrimEnd(percentChar);
                height = height.TrimEnd(percentChar);

                unit = RelativeUnit.Relative;
                scale = 0.01f;
            }
            else if (xRelative || yRelative || widthRelative || heightRelative)
            {
                throw new FormatException("If one coordinate is relative, all must be.");
            }

            return new RelativeRect(
                float.Parse(x, provider: provider) * scale,
                float.Parse(y, provider: provider) * scale,
                float.Parse(width, provider: provider) * scale,
                float.Parse(height, provider: provider) * scale,
                unit);
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out RelativeRect result)
    {
        return TryParse(utf8Text, null, out result);
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out RelativeRect result)
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
            return Rect.TryFormat(utf8Destination, out bytesWritten, default, provider);
        }
        else
        {
            char separator = TokenizerHelper.GetSeparatorFromFormatProvider(provider);
            return Utf8.TryWrite(
                utf8Destination,
                provider,
                $"{Rect.X * 100}%{separator} {Rect.Y * 100}%{separator} {Rect.Width * 100}%{separator} {Rect.Height * 100}%",
                out bytesWritten);
        }
    }
}
