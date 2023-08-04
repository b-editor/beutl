using Beutl.Graphics;

namespace Beutl.Media;

public interface IGeometryContext
{
    PathFillType FillType { get; set; }

    Point LastPoint { get; }

    Rect Bounds { get; }

    void Clear();

    void Transform(Matrix matrix);

    void MoveTo(Point point);

    void ArcTo(Size radius, float angle, bool isLargeArc, bool sweepClockwise, Point point);

    void ConicTo(Point controlPoint, Point endPoint, float weight);

    void CubicTo(Point controlPoint1, Point controlPoint2, Point endPoint);

    void LineTo(Point point);

    void QuadraticTo(Point controlPoint, Point endPoint);

    void Close();
}
