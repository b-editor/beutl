using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;

using Beutl.Converters;
using Beutl.Utilities;

namespace Beutl.Media;

/// <summary>
/// Represents the radii of a rectangle's corners.
/// </summary>
[JsonConverter(typeof(CornerRadiusJsonConverter))]
[TypeConverter(typeof(CornerRadiusConverter))]
public readonly struct CornerRadius : IEquatable<CornerRadius>, ITupleConvertible<CornerRadius, float>
{
    public CornerRadius(float uniformRadius)
    {
        TopLeft = TopRight = BottomLeft = BottomRight = uniformRadius;

    }

    public CornerRadius(float top, float bottom)
    {
        TopLeft = TopRight = top;
        BottomLeft = BottomRight = bottom;
    }

    public CornerRadius(float topLeft, float topRight, float bottomRight, float bottomLeft)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
    }

    public float TopLeft { get; }

    public float TopRight { get; }

    public float BottomRight { get; }

    public float BottomLeft { get; }

    /// <summary>
    /// Gets a value indicating whether all corner radii are set to 0.
    /// </summary>
    public bool IsEmpty => TopLeft.Equals(0) && IsUniform;

    /// <summary>
    /// Gets a value indicating whether all corner radii are equal.
    /// </summary>
    public bool IsUniform => TopLeft.Equals(TopRight) && BottomLeft.Equals(BottomRight) && TopRight.Equals(BottomRight);

    static int ITupleConvertible<CornerRadius, float>.TupleLength => 4;

    public bool Equals(CornerRadius other)
    {
        return TopLeft == other.TopLeft &&
               TopRight == other.TopRight &&
               BottomRight == other.BottomRight &&
               BottomLeft == other.BottomLeft;
    }

    public override bool Equals(object? obj)
    {
        return obj is CornerRadius other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(TopLeft, TopRight, BottomRight, BottomLeft);
    }

    public override string ToString()
    {
        return $"{TopLeft},{TopRight},{BottomRight},{BottomLeft}";
    }

    public static bool TryParse(string s, out CornerRadius cornerRadius)
    {
        return TryParse(s.AsSpan(), out cornerRadius);
    }

    public static bool TryParse(ReadOnlySpan<char> s, out CornerRadius cornerRadius)
    {
        try
        {
            cornerRadius = Parse(s);
            return true;
        }
        catch
        {
            cornerRadius = default;
            return false;
        }
    }

    public static CornerRadius Parse(string s)
    {
        return Parse(s.AsSpan());
    }

    public static CornerRadius Parse(ReadOnlySpan<char> s)
    {
        const string exceptionMessage = "Invalid CornerRadius.";

        using (var tokenizer = new RefStringTokenizer(s, CultureInfo.InvariantCulture, exceptionMessage))
        {
            if (tokenizer.TryReadSingle(out float a))
            {
                if (tokenizer.TryReadSingle(out float b))
                {
                    if (tokenizer.TryReadSingle(out float c))
                    {
                        return new CornerRadius(a, b, c, tokenizer.ReadSingle());
                    }

                    return new CornerRadius(a, b);
                }

                return new CornerRadius(a);
            }

            throw new FormatException(exceptionMessage);
        }
    }

    public CornerRadius WithTopLeft(float topLeft)
    {
        return new CornerRadius(topLeft, TopRight, BottomRight, BottomLeft);
    }

    public CornerRadius WithTopRight(float topRight)
    {
        return new CornerRadius(TopLeft, topRight, BottomRight, BottomLeft);
    }

    public CornerRadius WithBottomRight(float bottomRight)
    {
        return new CornerRadius(TopLeft, TopRight, bottomRight, BottomLeft);
    }

    public CornerRadius WithBottomLeft(float bottomLeft)
    {
        return new CornerRadius(TopLeft, TopRight, BottomRight, bottomLeft);
    }

    public CornerRadius WithTop(float top)
    {
        return new CornerRadius(top, top, BottomRight, BottomLeft);
    }

    public CornerRadius WithBottom(float bottom)
    {
        return new CornerRadius(TopLeft, TopRight, bottom, bottom);
    }

    static void ITupleConvertible<CornerRadius, float>.ConvertTo(CornerRadius self, Span<float> tuple)
    {
        tuple[0] = self.TopLeft;
        tuple[1] = self.TopRight;
        tuple[2] = self.BottomRight;
        tuple[3] = self.BottomLeft;
    }

    static void ITupleConvertible<CornerRadius, float>.ConvertFrom(Span<float> tuple, out CornerRadius self)
    {
        self = new CornerRadius(tuple[0], tuple[1], tuple[2], tuple[3]);
    }

    public static bool operator ==(CornerRadius left, CornerRadius right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CornerRadius left, CornerRadius right)
    {
        return !(left == right);
    }
}
