using Beutl.Graphics;

using SkiaSharp;

namespace Beutl.Media;

public sealed class GeometryContext : IGeometryContext, IDisposable
{
    public GeometryContext()
    {

    }

    public SKPath NativeObject { get; } = new();

    public bool IsDisposed { get; private set; }

    public PathFillType FillType
    {
        get => (PathFillType)NativeObject.FillType;
        set => NativeObject.FillType = (SKPathFillType)value;
    }

    public Point LastPoint => NativeObject.LastPoint.ToGraphicsPoint();

    public Rect Bounds => NativeObject.TightBounds.ToGraphicsRect();

    public void ArcTo(Size radius, float angle, bool isLargeArc, bool sweepClockwise, Point point)
    {
        NativeObject.ArcTo(
            r: new SKPoint(radius.Width, radius.Height),
            xAxisRotate: angle,
            largeArc: isLargeArc ? SKPathArcSize.Large : SKPathArcSize.Small,
            sweep: sweepClockwise ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise,
            xy: point.ToSKPoint());
    }

    public void Clear()
    {
        NativeObject.Reset();
    }

    public void Close()
    {
        NativeObject.Close();
    }

    public void ConicTo(Point controlPoint, Point endPoint, float weight)
    {
        NativeObject.ConicTo(controlPoint.ToSKPoint(), endPoint.ToSKPoint(), weight);
    }

    public void CubicTo(Point controlPoint1, Point controlPoint2, Point endPoint)
    {
        NativeObject.CubicTo(controlPoint1.ToSKPoint(), controlPoint2.ToSKPoint(), endPoint.ToSKPoint());
    }

    public void LineTo(Point point)
    {
        NativeObject.LineTo(point.ToSKPoint());
    }

    public void MoveTo(Point point)
    {
        NativeObject.MoveTo(point.ToSKPoint());
    }

    public void QuadraticTo(Point controlPoint, Point endPoint)
    {
        NativeObject.QuadTo(controlPoint.ToSKPoint(), endPoint.ToSKPoint());
    }

    public void Transform(Matrix matrix)
    {
        NativeObject.Transform(matrix.ToSKMatrix());
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            NativeObject.Dispose();
            GC.SuppressFinalize(this);
            IsDisposed = true;
        }
    }
}
