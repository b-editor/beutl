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
/// Represents a size in device pixels.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PixelSize"/> structure.
/// </remarks>
/// <param name="width">The width.</param>
/// <param name="height">The height.</param>
[JsonConverter(typeof(PixelSizeJsonConverter))]
[TypeConverter(typeof(PixelSizeConverter))]
public readonly struct PixelSize(int width, int height)
    : IEquatable<PixelSize>,
      IParsable<PixelSize>,
      ISpanParsable<PixelSize>,
      IEqualityOperators<PixelSize, PixelSize, bool>,
      ITupleConvertible<PixelSize, int>
{
    /// <summary>
    /// A size representing zero
    /// </summary>
    public static readonly PixelSize Empty = new(0, 0);

    /// <summary>
    /// Gets the aspect ratio of the size.
    /// </summary>
    public float AspectRatio => (float)Width / Height;

    /// <summary>
    /// Gets the width.
    /// </summary>
    public int Width { get; } = width;

    /// <summary>
    /// Gets the height.
    /// </summary>
    public int Height { get; } = height;

    static int ITupleConvertible<PixelSize, int>.TupleLength => 2;

    /// <summary>
    /// Checks for equality between two <see cref="PixelSize"/>s.
    /// </summary>
    /// <param name="left">The first size.</param>
    /// <param name="right">The second size.</param>
    /// <returns>True if the sizes are equal; otherwise false.</returns>
    public static bool operator ==(PixelSize left, PixelSize right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Checks for inequality between two <see cref="Size"/>s.
    /// </summary>
    /// <param name="left">The first size.</param>
    /// <param name="right">The second size.</param>
    /// <returns>True if the sizes are unequal; otherwise false.</returns>
    public static bool operator !=(PixelSize left, PixelSize right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Parses a <see cref="PixelSize"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="size">The <see cref="PixelSize"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(string s, out PixelSize size)
    {
        return TryParse(s.AsSpan(), out size);
    }

    /// <summary>
    /// Parses a <see cref="PixelSize"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="size">The <see cref="PixelSize"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out PixelSize size)
    {
        try
        {
            size = Parse(s);
            return true;
        }
        catch
        {
            size = default;
            return false;
        }
    }

    /// <summary>
    /// Parses a <see cref="PixelSize"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The <see cref="PixelSize"/>.</returns>
    public static PixelSize Parse(string s)
    {
        return Parse(s.AsSpan());
    }
    
    /// <summary>
    /// Parses a <see cref="PixelSize"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The <see cref="PixelSize"/>.</returns>
    public static PixelSize Parse(ReadOnlySpan<char> s)
    {
        using var tokenizer = new RefStringTokenizer(s, CultureInfo.InvariantCulture, exceptionMessage: "Invalid PixelSize.");

        return new PixelSize(
            tokenizer.ReadInt32(),
            tokenizer.ReadInt32());
    }

    /// <summary>
    /// Returns a boolean indicating whether the size is equal to the other given size.
    /// </summary>
    /// <param name="other">The other size to test equality against.</param>
    /// <returns>True if this size is equal to other; False otherwise.</returns>
    public bool Equals(PixelSize other)
    {
        return Width == other.Width && Height == other.Height;
    }

    /// <summary>
    /// Checks for equality between a size and an object.
    /// </summary>
    /// <param name="obj">The object.</param>
    /// <returns>
    /// True if <paramref name="obj"/> is a size that equals the current size.
    /// </returns>
    public override bool Equals(object? obj)
    {
        return obj is PixelSize other && Equals(other);
    }

    /// <summary>
    /// Returns a hash code for a <see cref="PixelSize"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Width, Height);
    }

    /// <summary>
    /// Returns a new <see cref="PixelSize"/> with the same height and the specified width.
    /// </summary>
    /// <param name="width">The width.</param>
    /// <returns>The new <see cref="PixelSize"/>.</returns>
    public PixelSize WithWidth(int width)
    {
        return new PixelSize(width, Height);
    }

    /// <summary>
    /// Returns a new <see cref="PixelSize"/> with the same width and the specified height.
    /// </summary>
    /// <param name="height">The height.</param>
    /// <returns>The new <see cref="PixelSize"/>.</returns>
    public PixelSize WithHeight(int height)
    {
        return new PixelSize(Width, height);
    }

    /// <summary>
    /// Converts the <see cref="PixelSize"/> to a device-independent <see cref="Size"/> using the
    /// specified scaling factor.
    /// </summary>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The device-independent size.</returns>
    public Size ToSize(float scale)
    {
        return new Size(Width / scale, Height / scale);
    }

    /// <summary>
    /// Converts the <see cref="PixelSize"/> to a device-independent <see cref="Size"/> using the
    /// specified scaling factor.
    /// </summary>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The device-independent size.</returns>
    public Size ToSize(Vector scale)
    {
        return new Size(Width / scale.X, Height / scale.Y);
    }

    /// <summary>
    /// Converts a <see cref="Size"/> to device pixels using the specified scaling factor.
    /// </summary>
    /// <param name="size">The size.</param>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The device-independent size.</returns>
    public static PixelSize FromSize(Size size, float scale)
    {
        return new PixelSize(
            (int)Math.Ceiling(size.Width * scale),
            (int)Math.Ceiling(size.Height * scale));
    }

    /// <summary>
    /// Converts a <see cref="Size"/> to device pixels using the specified scaling factor.
    /// </summary>
    /// <param name="size">The size.</param>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The device-independent size.</returns>
    public static PixelSize FromSize(Size size, Vector scale)
    {
        return new PixelSize(
            (int)Math.Ceiling(size.Width * scale.X),
            (int)Math.Ceiling(size.Height * scale.Y));
    }

    static PixelSize IParsable<PixelSize>.Parse(string s, IFormatProvider? provider)
    {
        return Parse(s);
    }

    static bool IParsable<PixelSize>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out PixelSize result)
    {
        result = default;
        return s != null && TryParse(s, out result);
    }

    static PixelSize ISpanParsable<PixelSize>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        return Parse(s);
    }

    static bool ISpanParsable<PixelSize>.TryParse([NotNullWhen(true)] ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out PixelSize result)
    {
        return TryParse(s, out result);
    }

    /// <summary>
    /// Returns the string representation of the size.
    /// </summary>
    /// <returns>The string representation of the size.</returns>
    public override string ToString()
    {
        return FormattableString.Invariant($"{Width}, {Height}");
    }

    static void ITupleConvertible<PixelSize, int>.ConvertTo(PixelSize self, Span<int> tuple)
    {
        tuple[0] = self.Width;
        tuple[1] = self.Height;
    }

    static void ITupleConvertible<PixelSize, int>.ConvertFrom(Span<int> tuple, out PixelSize self)
    {
        self = new PixelSize(tuple[0], tuple[1]);
    }
}
