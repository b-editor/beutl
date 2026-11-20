using System.Collections.Immutable;
using System.Text.Json.Serialization;

using Beutl.Converters;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics;

/// <summary>A rectangular mesh of control points in normalized input coordinates, in row-major order.</summary>
/// <remarks>The rest grid spans [0, 1] on each axis. Deformed points can extend outside that rectangle.</remarks>
[JsonConverter(typeof(MeshWarpGridJsonConverter))]
public sealed class MeshWarpGrid : IEquatable<MeshWarpGrid>
{
    internal const int MinCells = 1;
    internal const int MaxCells = 32;
    internal const int DefaultCells = 4;

    public static readonly MeshWarpGrid Default = CreateIdentity(DefaultCells, DefaultCells);

    public MeshWarpGrid(int rows, int columns, IEnumerable<Point>? points)
    {
        Rows = Math.Clamp(rows, MinCells, MaxCells);
        Columns = Math.Clamp(columns, MinCells, MaxCells);
        int count = (Rows + 1) * (Columns + 1);
        var builder = ImmutableArray.CreateBuilder<Point>(count);
        if (points is not null and not ImmutableArray<Point> { IsDefault: true })
        {
            foreach (Point point in points)
            {
                builder.Add(IsFinite(point) ? point : GetRestPoint(builder.Count));
                if (builder.Count == count)
                    break;
            }
        }

        while (builder.Count < count)
            builder.Add(GetRestPoint(builder.Count));

        Points = builder.MoveToImmutable();
    }

    private MeshWarpGrid(int rows, int columns, ImmutableArray<Point> points)
    {
        Rows = rows;
        Columns = columns;
        Points = points;
    }

    /// <summary>The number of vertical cells. The grid has one more row of points.</summary>
    public int Rows { get; }

    /// <summary>The number of horizontal cells. The grid has one more column of points.</summary>
    public int Columns { get; }

    public ImmutableArray<Point> Points { get; }

    public static MeshWarpGrid CreateIdentity(int rows, int columns) => new(rows, columns, points: null);

    public Point GetRestPoint(int row, int column) => new(column / (float)Columns, row / (float)Rows);

    private Point GetRestPoint(int index) => GetRestPoint(index / (Columns + 1), index % (Columns + 1));

    public MeshWarpGrid WithPoint(int index, Point point)
    {
        Point current = Points[index];
        return current == point || !IsFinite(point)
            ? this
            : new MeshWarpGrid(Rows, Columns, Points.SetItem(index, point));
    }

    public MeshWarpGrid WithPoint(int row, int column, Point point)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(row, Rows);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(column, Columns);
        return WithPoint(row * (Columns + 1) + column, point);
    }

    public bool IsIdentity(float epsilon = 1e-5f)
    {
        for (int i = 0; i < Points.Length; i++)
        {
            Point delta = Points[i] - GetRestPoint(i);
            if (MathF.Abs(delta.X) > epsilon || MathF.Abs(delta.Y) > epsilon)
                return false;
        }

        return true;
    }

    public MeshWarpGrid Resample(int newRows, int newColumns, MeshWarpInterpolation interpolation = MeshWarpInterpolation.Linear)
    {
        newRows = Math.Clamp(newRows, MinCells, MaxCells);
        newColumns = Math.Clamp(newColumns, MinCells, MaxCells);
        if (newRows == Rows && newColumns == Columns)
            return this;
        if (IsIdentity(0))
            return CreateIdentity(newRows, newColumns);

        int count = (newRows + 1) * (newColumns + 1);
        var points = ImmutableArray.CreateBuilder<Point>(count);
        for (int row = 0; row <= newRows; row++)
        {
            for (int column = 0; column <= newColumns; column++)
            {
                var rest = new Point(column / (float)newColumns, row / (float)newRows);
                Point point = Evaluate(rest, interpolation);
                points.Add(IsFinite(point) ? point : rest);
            }
        }

        return new MeshWarpGrid(newRows, newColumns, points.MoveToImmutable());
    }

    internal Point Evaluate(Point rest, MeshWarpInterpolation interpolation)
    {
        float x = rest.X * Columns;
        float y = rest.Y * Rows;
        int column = Math.Clamp((int)MathF.Floor(x), 0, Columns - 1);
        int row = Math.Clamp((int)MathF.Floor(y), 0, Rows - 1);
        float tx = x - column;
        float ty = y - row;

        if (interpolation == MeshWarpInterpolation.Smooth)
        {
            // Interpolate displacements so a clamped edge keeps its rest slope, even when nearby points move.
            // Interpolating the absolute coordinates with repeated endpoints would bend an undeformed edge.
            return rest + CatmullRom(
                SmoothRow(Math.Max(row - 1, 0), column, tx),
                SmoothRow(row, column, tx),
                SmoothRow(row + 1, column, tx),
                SmoothRow(Math.Min(row + 2, Rows), column, tx),
                ty);
        }

        int top = row * (Columns + 1) + column;
        int bottom = top + Columns + 1;
        return Lerp(Lerp(Points[top], Points[top + 1], tx), Lerp(Points[bottom], Points[bottom + 1], tx), ty);
    }

    private Point SmoothRow(int row, int column, float t)
        => CatmullRom(Displacement(row, Math.Max(column - 1, 0)), Displacement(row, column),
            Displacement(row, column + 1), Displacement(row, Math.Min(column + 2, Columns)), t);

    private Point Displacement(int row, int column) => Points[row * (Columns + 1) + column] - GetRestPoint(row, column);

    private static Point CatmullRom(Point p0, Point p1, Point p2, Point p3, float t)
    {
        if (t == 0) return p1;
        if (t == 1) return p2;

        Point startSlope = (p2 - p0) * 0.5f;
        Point endSlope = (p3 - p1) * 0.5f;
        Point delta = p2 - p1;
        Point quadratic = delta * 3 - startSlope * 2 - endSlope;
        Point cubic = startSlope + endSlope - delta * 2;
        return p1 + ((cubic * t + quadratic) * t + startSlope) * t;
    }

    internal static Point Lerp(Point from, Point to, float t)
        => t == 0 ? from : t == 1 ? to : from + (to - from) * t;

    internal static bool IsFinite(Point point) => float.IsFinite(point.X) && float.IsFinite(point.Y);

    public bool Equals(MeshWarpGrid? other)
        => ReferenceEquals(this, other)
           || other is not null && Rows == other.Rows && Columns == other.Columns
                                && Points.AsSpan().SequenceEqual(other.Points.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as MeshWarpGrid);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Rows);
        hash.Add(Columns);
        foreach (Point point in Points)
            hash.Add(point);

        return hash.ToHashCode();
    }
}
