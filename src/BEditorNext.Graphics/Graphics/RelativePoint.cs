using System.Globalization;

using BEditorNext.Utilities;

namespace BEditorNext.Graphics;

/// <summary>
/// Defines a point that may be defined relative to a containing element.
/// </summary>
public readonly struct RelativePoint : IEquatable<RelativePoint>
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
    /// <returns>The parsed <see cref="RelativePoint"/>.</returns>
    public static RelativePoint Parse(string s)
    {
        using (var tokenizer = new StringTokenizer(s, CultureInfo.InvariantCulture, exceptionMessage: "Invalid RelativePoint."))
        {
            string x = tokenizer.ReadString();
            string y = tokenizer.ReadString();

            RelativeUnit unit = RelativeUnit.Absolute;
            float scale = 1.0f;

            if (x.EndsWith("%"))
            {
                if (!y.EndsWith("%"))
                {
                    throw new FormatException("If one coordinate is relative, both must be.");
                }

                x = x.TrimEnd('%');
                y = y.TrimEnd('%');
                unit = RelativeUnit.Relative;
                scale = 0.01f;
            }

            return new RelativePoint(
                float.Parse(x, CultureInfo.InvariantCulture) * scale,
                float.Parse(y, CultureInfo.InvariantCulture) * scale,
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
             string.Format(CultureInfo.InvariantCulture, "{0}%, {1}%", Point.X * 100, Point.Y * 100);
    }
}
