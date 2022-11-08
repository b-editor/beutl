using System.Globalization;
using System.Text.Json.Serialization;

using Beutl.Converters;
using Beutl.Utilities;
using Beutl.Validation;

namespace Beutl.Media;

/// <summary>
/// Represents the radii of a rectangle's corners.
/// </summary>
[JsonConverter(typeof(CornerRadiusJsonConverter))]
[RangeValidatable(typeof(CornerRadiusRangeValidator))]
public readonly struct CornerRadius : IEquatable<CornerRadius>
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

    /// <summary>
    /// Radius of the top left corner.
    /// </summary>
    public float TopLeft { get; }

    /// <summary>
    /// Radius of the top right corner.
    /// </summary>
    public float TopRight { get; }

    /// <summary>
    /// Radius of the bottom right corner.
    /// </summary>
    public float BottomRight { get; }

    /// <summary>
    /// Radius of the bottom left corner.
    /// </summary>
    public float BottomLeft { get; }

    /// <summary>
    /// Gets a value indicating whether all corner radii are set to 0.
    /// </summary>
    public bool IsEmpty => TopLeft.Equals(0) && IsUniform;

    /// <summary>
    /// Gets a value indicating whether all corner radii are equal.
    /// </summary>
    public bool IsUniform => TopLeft.Equals(TopRight) && BottomLeft.Equals(BottomRight) && TopRight.Equals(BottomRight);

    /// <summary>
    /// Returns a boolean indicating whether the corner radius is equal to the other given corner radius.
    /// </summary>
    /// <param name="other">The other corner radius to test equality against.</param>
    /// <returns>True if this corner radius is equal to other; False otherwise.</returns>
    public bool Equals(CornerRadius other)
    {
        return TopLeft == other.TopLeft &&
               TopRight == other.TopRight &&
               BottomRight == other.BottomRight &&
               BottomLeft == other.BottomLeft;
    }

    /// <summary>
    /// Returns a boolean indicating whether the given Object is equal to this corner radius instance.
    /// </summary>
    /// <param name="obj">The Object to compare against.</param>
    /// <returns>True if the Object is equal to this corner radius; False otherwise.</returns>
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

    public static bool operator ==(CornerRadius left, CornerRadius right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CornerRadius left, CornerRadius right)
    {
        return !(left == right);
    }
}
