namespace Beutl.Media.Immutable;

public sealed class ImmutablePen : IPen
{
    public ImmutablePen(
        IBrush? brush,
        IReadOnlyList<float>? dashArray,
        float dashOffset,
        float thickness,
        float miterLimit = 10,
        StrokeCap strokeCap = StrokeCap.Flat,
        StrokeJoin strokeJoin = StrokeJoin.Miter,
        StrokeAlignment strokeAlignment = StrokeAlignment.Center)
    {
        Brush = brush;
        DashArray = dashArray;
        DashOffset = dashOffset;
        Thickness = thickness;
        MiterLimit = miterLimit;
        StrokeCap = strokeCap;
        StrokeJoin = strokeJoin;
        StrokeAlignment = strokeAlignment;
    }

    public IBrush? Brush { get; }

    public IReadOnlyList<float>? DashArray { get; }

    public float DashOffset { get; }

    public float Thickness { get; }

    public float MiterLimit { get; }

    public StrokeCap StrokeCap { get; }

    public StrokeJoin StrokeJoin { get; }

    public StrokeAlignment StrokeAlignment { get; }

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
