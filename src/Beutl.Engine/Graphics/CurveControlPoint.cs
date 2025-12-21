using System.Text.Json.Serialization;
using Beutl.Converters;

namespace Beutl.Graphics;

/// <summary>
/// Represents a control point on a curve with Bezier handles.
/// </summary>
[JsonConverter(typeof(CurveControlPointJsonConverter))]
public readonly struct CurveControlPoint : IEquatable<CurveControlPoint>
{
    /// <summary>
    /// Initializes a new instance with only the main point (no handles).
    /// </summary>
    public CurveControlPoint(float x, float y)
        : this(new Point(x, y), default, default)
    {
    }

    /// <summary>
    /// Initializes a new instance with only the main point (no handles).
    /// </summary>
    public CurveControlPoint(Point point)
        : this(point, default, default)
    {
    }

    /// <summary>
    /// Initializes a new instance with the main point and Bezier handles.
    /// </summary>
    public CurveControlPoint(Point point, Point leftHandle, Point rightHandle)
    {
        Point = point;
        LeftHandle = leftHandle;
        RightHandle = rightHandle;
    }

    /// <summary>
    /// Gets the main control point position (normalized 0-1 range).
    /// </summary>
    public Point Point { get; }

    /// <summary>
    /// Gets the left handle offset from the main point (for incoming tangent).
    /// This is added to Point to get the absolute position of the left handle.
    /// </summary>
    public Point LeftHandle { get; }

    /// <summary>
    /// Gets the right handle offset from the main point (for outgoing tangent).
    /// This is added to Point to get the absolute position of the right handle.
    /// </summary>
    public Point RightHandle { get; }

    /// <summary>
    /// Gets the absolute position of the left handle.
    /// </summary>
    public Point AbsoluteLeftHandle => new(Point.X + LeftHandle.X, Point.Y + LeftHandle.Y);

    /// <summary>
    /// Gets the absolute position of the right handle.
    /// </summary>
    public Point AbsoluteRightHandle => new(Point.X + RightHandle.X, Point.Y + RightHandle.Y);

    /// <summary>
    /// Gets a value indicating whether this control point has non-zero handles.
    /// </summary>
    public bool HasHandles => LeftHandle != default || RightHandle != default;

    /// <summary>
    /// Creates a new control point with the specified main point position.
    /// </summary>
    public CurveControlPoint WithPoint(Point point) => new(point, LeftHandle, RightHandle);

    /// <summary>
    /// Creates a new control point with the specified left handle.
    /// </summary>
    public CurveControlPoint WithLeftHandle(Point leftHandle) => new(Point, leftHandle, RightHandle);

    /// <summary>
    /// Creates a new control point with the specified right handle.
    /// </summary>
    public CurveControlPoint WithRightHandle(Point rightHandle) => new(Point, LeftHandle, rightHandle);

    /// <summary>
    /// Creates a new control point with symmetric handles (left = -right).
    /// </summary>
    public CurveControlPoint WithSymmetricHandles(Point rightHandle)
        => new(Point, new Point(-rightHandle.X, -rightHandle.Y), rightHandle);

    public bool Equals(CurveControlPoint other)
    {
        return Point == other.Point && LeftHandle == other.LeftHandle && RightHandle == other.RightHandle;
    }

    public override bool Equals(object? obj)
    {
        return obj is CurveControlPoint other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Point, LeftHandle, RightHandle);
    }

    public static bool operator ==(CurveControlPoint left, CurveControlPoint right) => left.Equals(right);

    public static bool operator !=(CurveControlPoint left, CurveControlPoint right) => !left.Equals(right);

    public override string ToString()
    {
        if (HasHandles)
        {
            return $"({Point.X},{Point.Y})[L({LeftHandle.X},{LeftHandle.Y}),R({RightHandle.X},{RightHandle.Y})]";
        }

        return $"({Point.X},{Point.Y})";
    }
}
