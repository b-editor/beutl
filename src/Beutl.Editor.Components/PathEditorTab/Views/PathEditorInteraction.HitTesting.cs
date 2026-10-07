using Avalonia;
using Beutl.Composition;
using Beutl.Controls;
using Beutl.Media;
using BtlPoint = Beutl.Graphics.Point;
using CubicBezierSegment = Beutl.Media.CubicBezierSegment;
using LineSegment = Beutl.Media.LineSegment;
using PathFigure = Beutl.Media.PathFigure;
using PathSegment = Beutl.Media.PathSegment;

namespace Beutl.Editor.Components.PathEditorTab.Views;

internal sealed partial class PathEditorInteraction
{
    private bool TryLocal(Point point, out BtlPoint local)
    {
        local = default;
        if (_view.Scale <= 0 || !_view.Matrix.TryInvert(out Matrix inverse)) return false;
        Point p = inverse.Transform(point / _view.Scale);
        local = new((float)p.X, (float)p.Y);
        return float.IsFinite(local.X) && float.IsFinite(local.Y);
    }

    private Point Screen(BtlPoint point) => _view.Matrix.Transform(point.ToAvaPoint()) * _view.Scale;

    private (int index, float t, Point point)? HitEdge(Point point)
    {
        if (Context?.PathFigure.Value is not { Segments.Count: > 0 } figure) return null;
        var context = Composition;
        double best = HitRadius * HitRadius;
        (int, float, Point)? result = null;
        for (int i = PathEditingOperations.FirstEdge(figure, context); i < PathEditingOperations.EdgeCount(figure, context); i++)
        {
            if (i < figure.Segments.Count && figure.Segments[i] is not (LineSegment or CubicBezierSegment
                or Beutl.Media.QuadraticBezierSegment or ConicSegment or Beutl.Media.ArcSegment)) continue;
            BtlPoint start = PathEditingOperations.Start(figure, i, context);
            if (IsOutsideControlHull(figure, i, point, start, context)) continue;
            Visit(0, Screen(start), 1, Evaluate(1), 0);

            Point Evaluate(float t) => Screen(PathEditingOperations.EvaluateEdge(figure, i, t, context));

            void Visit(float from, Point a, float to, Point b, int depth)
            {
                float middle = (from + to) / 2;
                Point mid = Evaluate(middle);
                // Keep the approximation within half a screen pixel at any zoom.
                // Compare matching parameters, not just distance to the chord:
                // collinear Bezier controls can still have non-linear parameter speed.
                // Quarter samples also catch S-curves whose midpoint lies on the chord.
                Vector chord = b - a;
                if (depth < 16 && (((Vector)(mid - (a + chord * .5))).SquaredLength > .25
                    || ((Vector)(Evaluate((from + middle) / 2) - (a + chord * .25))).SquaredLength > .25
                    || ((Vector)(Evaluate((middle + to) / 2) - (a + chord * .75))).SquaredLength > .25))
                {
                    Visit(from, a, middle, mid, depth + 1);
                    Visit(middle, mid, to, b, depth + 1);
                    return;
                }
                var (distance, fraction, nearest) = Project(point, a, b);
                float t = (float)(from + (to - from) * fraction);
                if (distance < best && t > 0 && t < 1) { best = distance; result = (i, t, nearest); }
            }
        }
        return result;

        static (double distance, double fraction, Point nearest) Project(Point p, Point a, Point b)
        {
            Vector direction = b - a;
            double length = direction.SquaredLength;
            double fraction = length > 0 ? Math.Clamp(Vector.Dot(p - a, direction) / length, 0, 1) : 0;
            Point nearest = a + direction * fraction;
            return (((Vector)(p - nearest)).SquaredLength, fraction, nearest);
        }
    }

    // Cheap rejection before the adaptive subdivision in HitEdge.
    private bool IsOutsideControlHull(PathFigure figure, int i, Point point, BtlPoint start, CompositionContext context)
    {
        if (i < figure.Segments.Count && figure.Segments[i] is not Beutl.Media.ArcSegment
            && (figure.Segments[i] is not ConicSegment conic || conic.Weight.GetValue(context) >= 0))
        {
            // Non-negative rational curves and Beziers stay inside their control hull.
            // Bound it in screen coordinates before doing any adaptive subdivision.
            Point first = Screen(start);
            double left = first.X, right = first.X, top = first.Y, bottom = first.Y;
            var segment = figure.Segments[i];
            Include(segment.GetEndPoint().GetValue(context));
            switch (segment)
            {
                case CubicBezierSegment cubic:
                    Include(cubic.ControlPoint1.GetValue(context));
                    Include(cubic.ControlPoint2.GetValue(context));
                    break;
                case Beutl.Media.QuadraticBezierSegment quadratic:
                    Include(quadratic.ControlPoint.GetValue(context));
                    break;
                case ConicSegment rational:
                    Include(rational.ControlPoint.GetValue(context));
                    break;
            }
            if (point.X < left - HitRadius || point.X > right + HitRadius || point.Y < top - HitRadius || point.Y > bottom + HitRadius) return true;

            void Include(BtlPoint value)
            {
                Point p = Screen(value);
                left = Math.Min(left, p.X); right = Math.Max(right, p.X);
                top = Math.Min(top, p.Y); bottom = Math.Max(bottom, p.Y);
            }
        }
        return false;
    }

    private void Insert(int index, float t)
    {
        if (Context?.PathFigure.Value is not { } figure) return;
        PathSegment? inserted = null;
        Mutate(() => inserted = PathEditingOperations.Split(figure, index, t, Composition));
        if (inserted != null) SelectOnly(inserted);
    }
}
