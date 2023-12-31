using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Text.Unicode;

using Beutl.Converters;
using Beutl.Media;
using Beutl.Utilities;

namespace Beutl.Graphics;

/// <summary>
/// Defines a size.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="Size"/> structure.
/// </remarks>
/// <param name="width">The width.</param>
/// <param name="height">The height.</param>
[JsonConverter(typeof(SizeJsonConverter))]
[TypeConverter(typeof(SizeConverter))]
public readonly struct Size(float width, float height)
    : IEquatable<Size>,
      IParsable<Size>,
      ISpanParsable<Size>,
      ISpanFormattable,
      IUtf8SpanFormattable,
      IUtf8SpanParsable<Size>,
      IEqualityOperators<Size, Size, bool>,
      IMultiplyOperators<Size, Vector, Size>,
      IDivisionOperators<Size, Vector, Size>,
      IDivisionOperators<Size, Size, Vector>,
      IMultiplyOperators<Size, float, Size>,
      IDivisionOperators<Size, float, Size>,
      IAdditionOperators<Size, Size, Size>,
      ISubtractionOperators<Size, Size, Size>,
      IAdditionOperators<Size, Thickness, Size>,
      ISubtractionOperators<Size, Thickness, Size>,
      ITupleConvertible<Size, float>
{
    /// <summary>
    /// A size representing infinity.
    /// </summary>
    public static readonly Size Infinity = new(float.PositiveInfinity, float.PositiveInfinity);

    /// <summary>
    /// A size representing zero
    /// </summary>
    public static readonly Size Empty = new(0, 0);

    /// <summary>
    /// Gets the aspect ratio of the size.
    /// </summary>
    public float AspectRatio => Width / Height;

    /// <summary>
    /// Gets the width.
    /// </summary>
    public float Width { get; } = width;

    /// <summary>
    /// Gets the height.
    /// </summary>
    public float Height { get; } = height;

    /// <summary>
    /// Gets a value indicating whether the Width and Height values are zero.
    /// </summary>
    public bool IsDefault => (Width == 0) && (Height == 0);

    static int ITupleConvertible<Size, float>.TupleLength => 2;

    /// <summary>
    /// Checks for equality between two <see cref="Size"/>s.
    /// </summary>
    /// <param name="left">The first size.</param>
    /// <param name="right">The second size.</param>
    /// <returns>True if the sizes are equal; otherwise false.</returns>
    public static bool operator ==(Size left, Size right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Checks for inequality between two <see cref="Size"/>s.
    /// </summary>
    /// <param name="left">The first size.</param>
    /// <param name="right">The second size.</param>
    /// <returns>True if the sizes are unequal; otherwise false.</returns>
    public static bool operator !=(Size left, Size right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Scales a size.
    /// </summary>
    /// <param name="size">The size</param>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The scaled size.</returns>
    public static Size operator *(Size size, Vector scale)
    {
        return new Size(size.Width * scale.X, size.Height * scale.Y);
    }

    /// <summary>
    /// Scales a size.
    /// </summary>
    /// <param name="size">The size</param>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The scaled size.</returns>
    public static Size operator /(Size size, Vector scale)
    {
        return new Size(size.Width / scale.X, size.Height / scale.Y);
    }

    /// <summary>
    /// Divides a size by another size to produce a scaling factor.
    /// </summary>
    /// <param name="left">The first size</param>
    /// <param name="right">The second size.</param>
    /// <returns>The scaled size.</returns>
    public static Vector operator /(Size left, Size right)
    {
        return new Vector(left.Width / right.Width, left.Height / right.Height);
    }

    /// <summary>
    /// Scales a size.
    /// </summary>
    /// <param name="size">The size</param>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The scaled size.</returns>
    public static Size operator *(Size size, float scale)
    {
        return new Size(size.Width * scale, size.Height * scale);
    }

    /// <summary>
    /// Scales a size.
    /// </summary>
    /// <param name="size">The size</param>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The scaled size.</returns>
    public static Size operator /(Size size, float scale)
    {
        return new Size(size.Width / scale, size.Height / scale);
    }

    public static Size operator +(Size size, Size toAdd)
    {
        return new Size(size.Width + toAdd.Width, size.Height + toAdd.Height);
    }

    public static Size operator -(Size size, Size toSubtract)
    {
        return new Size(size.Width - toSubtract.Width, size.Height - toSubtract.Height);
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
    /// Parses a <see cref="Size"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="size">The <see cref="Size"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(string s, out Size size)
    {
        return TryParse(s.AsSpan(), out size);
    }

    /// <summary>
    /// Parses a <see cref="Size"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="size">The <see cref="Size"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out Size size)
    {
        return TryParse(s, null, out size);
    }

    /// <summary>
    /// Parses a <see cref="Size"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The <see cref="Size"/>.</returns>
    public static Size Parse(string s)
    {
        return Parse(s.AsSpan());
    }

    /// <summary>
    /// Parses a <see cref="Size"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The <see cref="Size"/>.</returns>
    public static Size Parse(ReadOnlySpan<char> s)
    {
        return Parse(s, null);
    }

    /// <summary>
    /// Constrains the size.
    /// </summary>
    /// <param name="constraint">The size to constrain to.</param>
    /// <returns>The constrained size.</returns>
    public Size Constrain(Size constraint)
    {
        return new Size(
            MathF.Min(Width, constraint.Width),
            MathF.Min(Height, constraint.Height));
    }

    /// <summary>
    /// Deflates the size by a <see cref="Thickness"/>.
    /// </summary>
    /// <param name="thickness">The thickness.</param>
    /// <returns>The deflated size.</returns>
    /// <remarks>The deflated size cannot be less than 0.</remarks>
    public Size Deflate(Thickness thickness)
    {
        return new Size(
            MathF.Max(0, Width - thickness.Left - thickness.Right),
            MathF.Max(0, Height - thickness.Top - thickness.Bottom));
    }

    /// <summary>
    /// Deflates the size by a <see cref="Thickness"/>.
    /// </summary>
    /// <param name="thickness">The thickness.</param>
    /// <returns>The deflated size.</returns>
    /// <remarks>The deflated size cannot be less than 0.</remarks>
    public Size Deflate(float thickness)
    {
        return new Size(
            MathF.Max(0, Width - (thickness * 2)),
            MathF.Max(0, Height - (thickness * 2)));
    }

    /// <summary>
    /// Returns a boolean indicating whether the size is equal to the other given size (bitwise).
    /// </summary>
    /// <param name="other">The other size to test equality against.</param>
    /// <returns>True if this size is equal to other; False otherwise.</returns>
    public bool Equals(Size other)
    {
        return Width == other.Width &&
               Height == other.Height;
    }

    /// <summary>
    /// Returns a boolean indicating whether the size is equal to the other given size (numerically).
    /// </summary>
    /// <param name="other">The other size to test equality against.</param>
    /// <returns>True if this size is equal to other; False otherwise.</returns>
    public bool NearlyEquals(Size other)
    {
        return MathUtilities.AreClose(Width, other.Width) &&
               MathUtilities.AreClose(Height, other.Height);
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
        return obj is Size other && Equals(other);
    }

    /// <summary>
    /// Returns a hash code for a <see cref="Size"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Width, Height);
    }

    /// <summary>
    /// Inflates the size by a <see cref="Thickness"/>.
    /// </summary>
    /// <param name="thickness">The thickness.</param>
    /// <returns>The inflated size.</returns>
    public Size Inflate(Thickness thickness)
    {
        return new Size(
            Width + thickness.Left + thickness.Right,
            Height + thickness.Top + thickness.Bottom);
    }

    /// <summary>
    /// Inflates the size by a <see cref="Thickness"/>.
    /// </summary>
    /// <param name="thickness">The thickness.</param>
    /// <returns>The inflated size.</returns>
    public Size Inflate(float thickness)
    {
        return new Size(
            Width + (thickness * 2),
            Height + (thickness * 2));
    }

    /// <summary>
    /// Returns a new <see cref="Size"/> with the same height and the specified width.
    /// </summary>
    /// <param name="width">The width.</param>
    /// <returns>The new <see cref="Size"/>.</returns>
    public Size WithWidth(float width)
    {
        return new Size(width, Height);
    }

    /// <summary>
    /// Returns a new <see cref="Size"/> with the same width and the specified height.
    /// </summary>
    /// <param name="height">The height.</param>
    /// <returns>The new <see cref="Size"/>.</returns>
    public Size WithHeight(float height)
    {
        return new Size(Width, height);
    }

    public PixelSize Ceiling()
    {
        return new PixelSize((int)MathF.Ceiling(Width), (int)MathF.Ceiling(Height));
    }

    /// <summary>
    /// Returns the string representation of the size.
    /// </summary>
    /// <returns>The string representation of the size.</returns>
    public override string ToString()
    {
        return FormattableString.Invariant($"{Width}, {Height}");
    }

    /// <summary>
    /// Deconstructs the size into its Width and Height values.
    /// </summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    public void Deconstruct(out float width, out float height)
    {
        width = Width;
        height = Height;
    }

    public static Size Parse(string s, IFormatProvider? provider)
    {
        return Parse(s.AsSpan(), provider);
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Size result)
    {
        return TryParse(s.AsSpan(), provider, out result);
    }

    public static Size Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        using var tokenizer = new RefStringTokenizer(s, provider ?? CultureInfo.InvariantCulture, exceptionMessage: "Invalid Size.");
        return new Size(
            tokenizer.ReadSingle(),
            tokenizer.ReadSingle());
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Size result)
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

    static void ITupleConvertible<Size, float>.ConvertTo(Size self, Span<float> tuple)
    {
        tuple[0] = self.Width;
        tuple[1] = self.Height;
    }

    static void ITupleConvertible<Size, float>.ConvertFrom(Span<float> tuple, out Size self)
    {
        self = new Size(tuple[0], tuple[1]);
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        char separator = TokenizerHelper.GetSeparatorFromFormatProvider(provider);
        return MemoryExtensions.TryWrite(destination, provider, $"{Width}{separator} {Height}", out charsWritten);
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        char separator = TokenizerHelper.GetSeparatorFromFormatProvider(formatProvider);
        return string.Create(formatProvider, $"{Width}{separator} {Height}");
    }

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        char separator = TokenizerHelper.GetSeparatorFromFormatProvider(provider);
        return Utf8.TryWrite(utf8Destination, provider, $"{Width}{separator} {Height}", out bytesWritten);
    }

    public static Size Parse(ReadOnlySpan<byte> utf8Text)
    {
        return Parse(utf8Text, null);
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out Size result)
    {
        try
        {
            result = Parse(utf8Text, null);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    public static Size Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
    {
        using var tokenizer = new RefUtf8StringTokenizer(utf8Text, provider ?? CultureInfo.InvariantCulture, exceptionMessage: "Invalid Size.");
        return new Size(
            tokenizer.ReadSingle(),
            tokenizer.ReadSingle());
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out Size result)
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
}
