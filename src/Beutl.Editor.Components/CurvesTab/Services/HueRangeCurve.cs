using Beutl.Graphics;

namespace Beutl.Editor.Components.CurvesTab.Services;

// Shapes a hue curve around one hue, as the curves tab's color range does. Inside the range the curve becomes a smooth
// bump from its own value at each edge to the given value at the center; outside it the curve keeps its points and its
// shape exactly, so the colors there do not change. Hue 0 and hue 1 are the same red: a range that crosses them goes on
// at the other end of the curve, and both ends then take the bump's value there. An edge inside a curved piece whose x
// goes back and forth cannot be cut without changing the colors outside, so such a curve is left as it is.
internal static class HueRangeCurve
{
    // A range spans at most 300 of the 360 degrees, so its two edges never meet and only one can cross the ends.
    public const float MinWidth = 1f / 360;
    public const float MaxWidth = 300f / 360;

    // Points this close count as one, and an edge or a center this close to an end of the curve sits on it.
    private const float Tolerance = 1e-4f;

    // The step that measures the curve's slope outside an edge, and the steepest slope a bump starts with.
    private const float SlopeStep = 1e-3f;
    private const float MaxSlope = 4;

    // The shaped curve, or null where an edge of the range falls inside a curved piece whose x goes back and forth.
    public static CurveMap? Shape(CurveMap curve, float center, float width, float value)
    {
        float half = Math.Clamp(width, MinWidth, MaxWidth) / 2;
        center -= MathF.Floor(center);
        if (center < Tolerance || center > 1 - Tolerance)
            center = 0;

        // The edges in the curve's own terms, then where they fall on it: an edge past an end goes on from the other.
        float left = SnapToEnd(center - half);
        float right = SnapToEnd(center + half);
        float leftX = left < 0 ? left + 1 : left;
        float rightX = right > 1 ? right - 1 : right;
        bool crossesEnds = left < 0 || right > 1;

        // The bump leaves each edge along the curve, so that it bends in smoothly.
        float leftSlope = Slope(curve, leftX, -1);
        float rightSlope = Slope(curve, rightX, 1);

        List<CurveControlPoint> points = WithEnds(curve.Points);
        if (!SplitAt(points, leftX) || !SplitAt(points, rightX))
            return null;

        points.RemoveAll(point => crossesEnds
            ? point.Point.X > leftX + Tolerance || point.Point.X < rightX - Tolerance
            : point.Point.X > leftX + Tolerance && point.Point.X < rightX - Tolerance);
        int leftEdge = IndexAt(points, leftX);
        int rightEdge = IndexAt(points, rightX);
        float leftY = points[leftEdge].Point.Y;
        float rightY = points[rightEdge].Point.Y;

        // Each half is one cubic in the range's own terms, its control points a third of the half apart. Evenly spaced
        // x values keep x(t) linear, so a half crosses an end of the curve at a parameter known in advance.
        float third = half / 3;
        var leftHalf = new Cubic(
            new Point(left, leftY),
            new Point(left + third, leftY + leftSlope * third),
            new Point(center - third, value),
            new Point(center, value));
        var rightHalf = new Cubic(
            new Point(center, value),
            new Point(center + third, value),
            new Point(right - third, rightY - rightSlope * third),
            new Point(right, rightY));

        if (left < 0 && center > 0)
        {
            (Cubic beforeEnd, Cubic afterEnd) = leftHalf.Split(-left / half);
            points[leftEdge] = points[leftEdge].WithRightHandle(beforeEnd.P1 - beforeEnd.P0);
            AddEnds(points, beforeEnd, afterEnd);
            points.Add(new CurveControlPoint(new Point(center, value), afterEnd.P2 - afterEnd.P3, rightHalf.P1 - rightHalf.P0));
            points[rightEdge] = points[rightEdge].WithLeftHandle(rightHalf.P2 - rightHalf.P3);
        }
        else if (right > 1)
        {
            (Cubic beforeEnd, Cubic afterEnd) = rightHalf.Split((1 - center) / half);
            points[leftEdge] = points[leftEdge].WithRightHandle(leftHalf.P1 - leftHalf.P0);
            points.Add(new CurveControlPoint(new Point(center, value), leftHalf.P2 - leftHalf.P3, beforeEnd.P1 - beforeEnd.P0));
            AddEnds(points, beforeEnd, afterEnd);
            points[rightEdge] = points[rightEdge].WithLeftHandle(afterEnd.P2 - afterEnd.P3);
        }
        else if (center == 0)
        {
            // The center is the red at both ends: the left half ends at hue 1 and the right half starts at hue 0.
            points[leftEdge] = points[leftEdge].WithRightHandle(leftHalf.P1 - leftHalf.P0);
            AddEnds(points, leftHalf, rightHalf);
            points[rightEdge] = points[rightEdge].WithLeftHandle(rightHalf.P2 - rightHalf.P3);
        }
        else
        {
            points[leftEdge] = points[leftEdge].WithRightHandle(leftHalf.P1 - leftHalf.P0);
            points.Add(new CurveControlPoint(new Point(center, value), leftHalf.P2 - leftHalf.P3, rightHalf.P1 - rightHalf.P0));
            points[rightEdge] = points[rightEdge].WithLeftHandle(rightHalf.P2 - rightHalf.P3);
        }

        return new CurveMap(points);
    }

    // The points at hue 1 and hue 0 where the bump meets itself across the ends: beforeEnd ends at hue 1 and afterEnd
    // starts at hue 0, at one value. The handles that would point past the ends stay empty.
    private static void AddEnds(List<CurveControlPoint> points, Cubic beforeEnd, Cubic afterEnd)
    {
        float y = beforeEnd.P3.Y;
        points.Add(new CurveControlPoint(new Point(1, y), beforeEnd.P2 - beforeEnd.P3, default));
        points.Add(new CurveControlPoint(new Point(0, y), default, afterEnd.P1 - afterEnd.P0));
    }

    private static float SnapToEnd(float x)
    {
        if (MathF.Abs(x) < Tolerance) return 0;
        if (MathF.Abs(x - 1) < Tolerance) return 1;
        return x;
    }

    // The curve's slope just outside x, on the side direction points to. Past an end it reads the other end, the same
    // red, unless the two ends disagree; then the bump starts level.
    private static float Slope(CurveMap curve, float x, int direction)
    {
        float outside = x + direction * SlopeStep;
        if (outside < 0 || outside > 1)
        {
            if (MathF.Abs(curve.Evaluate(0) - curve.Evaluate(1)) > Tolerance)
                return 0;

            outside -= MathF.Floor(outside);
        }

        float slope = (curve.Evaluate(x) - curve.Evaluate(outside)) / -(direction * SlopeStep);
        return float.IsFinite(slope) ? Math.Clamp(slope, -MaxSlope, MaxSlope) : 0;
    }

    // The curve holds its first value before its first point and its last after its last, so points at both ends
    // change nothing; the range needs them to cross the ends.
    private static List<CurveControlPoint> WithEnds(IReadOnlyList<CurveControlPoint> source)
    {
        var points = new List<CurveControlPoint>(source.Count + 7);
        if (source.Count == 0)
        {
            points.Add(new CurveControlPoint(0, 0));
            points.Add(new CurveControlPoint(1, 1));
            return points;
        }

        if (source[0].Point.X > 0)
            points.Add(new CurveControlPoint(0, source[0].Point.Y));
        points.AddRange(source);
        if (source[^1].Point.X < 1)
            points.Add(new CurveControlPoint(1, source[^1].Point.Y));
        return points;
    }

    private static int IndexAt(List<CurveControlPoint> points, float x)
        => points.FindIndex(point => MathF.Abs(point.Point.X - x) <= Tolerance);

    // Puts a point at x without changing the curve's shape: a straight piece stays straight, and a curved one is cut in
    // two halves that trace it exactly. A point already at x stays as it is. A curved piece whose x goes back and forth,
    // as when a handle reaches past the next point, is left whole and false returned: CurveMap.Evaluate finds one of the
    // piece's crossings of an x by bisection, and the halves of a cut would find others, changing the curve outside x.
    private static bool SplitAt(List<CurveControlPoint> points, float x)
    {
        if (IndexAt(points, x) >= 0)
            return true;

        int next = points.FindIndex(point => point.Point.X > x);
        CurveControlPoint start = points[next - 1];
        CurveControlPoint end = points[next];
        if (!start.HasHandles && !end.HasHandles)
        {
            float t = (x - start.Point.X) / (end.Point.X - start.Point.X);
            points.Insert(next, new CurveControlPoint(x, start.Point.Y + (end.Point.Y - start.Point.Y) * t));
            return true;
        }

        var piece = new Cubic(start.Point, start.AbsoluteRightHandle, end.AbsoluteLeftHandle, end.Point);
        if (!piece.IsMonotonicInX)
            return false;

        (Cubic first, Cubic second) = piece.Split(piece.ParameterAtX(x));
        points[next - 1] = start.WithRightHandle(first.P1 - first.P0);
        points[next] = end.WithLeftHandle(second.P2 - second.P3);
        points.Insert(next, new CurveControlPoint(new Point(x, first.P3.Y), first.P2 - first.P3, second.P1 - second.P0));
        return true;
    }

    private readonly record struct Cubic(Point P0, Point P1, Point P2, Point P3)
    {
        // Whether x never goes back along the curve. Its derivative is a quadratic in Bernstein form with the
        // coefficients below, which stays at or above zero on the whole piece exactly when this holds.
        public bool IsMonotonicInX
        {
            get
            {
                float a = P1.X - P0.X;
                float b = P2.X - P1.X;
                float c = P3.X - P2.X;
                return a >= 0 && c >= 0 && (b >= 0 || b * b <= a * c + 1e-9f);
            }
        }

        // De Casteljau's construction: the two halves trace this curve exactly.
        public (Cubic First, Cubic Second) Split(float t)
        {
            Point a = Lerp(P0, P1, t);
            Point b = Lerp(P1, P2, t);
            Point c = Lerp(P2, P3, t);
            Point d = Lerp(a, b, t);
            Point e = Lerp(b, c, t);
            Point middle = Lerp(d, e, t);
            return (new Cubic(P0, a, d, middle), new Cubic(middle, e, c, P3));
        }

        // The parameter where the curve reaches x, found as CurveMap.Evaluate finds it.
        public float ParameterAtX(float x)
        {
            float low = 0;
            float high = 1;
            float middle = 0.5f;
            for (int i = 0; i < 30; i++)
            {
                middle = (low + high) / 2;
                float at = XAt(middle);
                if (MathF.Abs(at - x) < 1e-7f)
                    break;

                if (at < x)
                    low = middle;
                else
                    high = middle;
            }

            return middle;
        }

        private float XAt(float t)
        {
            float u = 1 - t;
            return u * u * u * P0.X + 3 * u * u * t * P1.X + 3 * u * t * t * P2.X + t * t * t * P3.X;
        }

        private static Point Lerp(Point a, Point b, float t) => a + (b - a) * t;
    }
}
