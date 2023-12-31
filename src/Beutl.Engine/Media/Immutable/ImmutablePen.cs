namespace Beutl.Media.Immutable;

public sealed class ImmutablePen(
    IBrush? brush,
    IReadOnlyList<float>? dashArray,
    float dashOffset,
    float thickness,
    float miterLimit = 10,
    StrokeCap strokeCap = StrokeCap.Flat,
    StrokeJoin strokeJoin = StrokeJoin.Miter,
    StrokeAlignment strokeAlignment = StrokeAlignment.Center)
    : IPen
{
    public IBrush? Brush { get; } = brush;

    public IReadOnlyList<float>? DashArray { get; } = dashArray;

    public float DashOffset { get; } = dashOffset;

    public float Thickness { get; } = thickness;

    public float MiterLimit { get; } = miterLimit;

    public StrokeCap StrokeCap { get; } = strokeCap;

    public StrokeJoin StrokeJoin { get; } = strokeJoin;

    public StrokeAlignment StrokeAlignment { get; } = strokeAlignment;

    public override bool Equals(object? obj)
    {
        return Equals(obj as IPen);
    }

    public bool Equals(IPen? other)
    {
        return other is not null
            && EqualityComparer<IBrush?>.Default.Equals(Brush, other.Brush)
            && ((DashArray != null && other.DashArray != null && DashArray.SequenceEqual(other.DashArray))
            || DashArray == null && other.DashArray == null)
            && DashOffset == other.DashOffset
            && Thickness == other.Thickness
            && MiterLimit == other.MiterLimit
            && StrokeCap == other.StrokeCap
            && StrokeJoin == other.StrokeJoin
            && StrokeAlignment == other.StrokeAlignment;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Brush, DashArray, DashOffset, Thickness, MiterLimit, StrokeCap, StrokeJoin, StrokeAlignment);
    }
}
