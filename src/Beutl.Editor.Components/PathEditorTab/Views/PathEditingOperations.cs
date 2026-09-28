using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Serialization;
using SkiaSharp;

namespace Beutl.Editor.Components.PathEditorTab.Views;

// Topology edits must not silently bake animation or expressions into the current frame.
internal static class PathEditingOperations
{
    internal static bool IsStatic(IProperty property) => property.Animation == null && !property.HasExpression;

    internal static Point Lerp(Point a, Point b, float t) => new(float.Lerp(a.X, b.X, t), float.Lerp(a.Y, b.Y, t));

    internal static Point Start(PathFigure figure, int index, CompositionContext context)
    {
        if (index > 0) return figure.Segments[index - 1].GetEndPoint().GetValue(context);
        Point start = figure.StartPoint.GetValue(context);
        return !start.IsInvalid ? start : figure.Segments[^1].GetEndPoint().GetValue(context);
    }

    internal static int FirstEdge(PathFigure figure, CompositionContext context) =>
        figure.StartPoint.GetValue(context).IsInvalid && !figure.IsClosed.GetValue(context) ? 1 : 0;

    internal static int EdgeCount(PathFigure figure, CompositionContext context) => figure.Segments.Count
        + (figure.Segments.Count > 0 && figure.IsClosed.GetValue(context)
            && !figure.StartPoint.GetValue(context).IsInvalid ? 1 : 0);

    internal static Point EvaluateEdge(PathFigure figure, int index, float t, CompositionContext context) =>
        index == figure.Segments.Count
            ? Lerp(Start(figure, index, context), figure.StartPoint.GetValue(context), t)
            : Evaluate(Start(figure, index, context), figure.Segments[index], t, context);

    internal static Point Evaluate(Point start, PathSegment segment, float t, CompositionContext context)
    {
        Point end = segment.GetEndPoint().GetValue(context);
        switch (segment)
        {
            case CubicBezierSegment c:
                Point a = Lerp(start, c.ControlPoint1.GetValue(context), t);
                Point b = Lerp(c.ControlPoint1.GetValue(context), c.ControlPoint2.GetValue(context), t);
                Point d = Lerp(c.ControlPoint2.GetValue(context), end, t);
                return Lerp(Lerp(a, b, t), Lerp(b, d, t), t);
            case QuadraticBezierSegment q:
                return Lerp(Lerp(start, q.ControlPoint.GetValue(context), t), Lerp(q.ControlPoint.GetValue(context), end, t), t);
            case ConicSegment c:
                float w = c.Weight.GetValue(context), u = 1 - t;
                Point control = c.ControlPoint.GetValue(context);
                float denominator = u * u + 2 * w * u * t + t * t;
                return new((u * u * start.X + 2 * w * u * t * control.X + t * t * end.X) / denominator,
                    (u * u * start.Y + 2 * w * u * t * control.Y + t * t * end.Y) / denominator);
            case ArcSegment arc:
                if (t == 0) return start;
                if (t == 1) return end;
                return GetArc(start, arc, context)?.At(t) ?? Lerp(start, end, t);
            default:
                return Lerp(start, end, t);
        }
    }

    internal static bool CanSplit(PathFigure figure, int index, bool preserveSingleKeyframe = true)
    {
        if (index < 0 || index > figure.Segments.Count || figure.Segments.Count == 0) return false;
        // A single keyframe is constant at every time, but is still retained as an
        // animation when deriving the new points. Moving curves and expressions
        // require a different representation and must not be baked to this frame.
        bool allowKeyframe = preserveSingleKeyframe
            && (index == figure.Segments.Count || figure.Segments[index] is not ArcSegment);
        bool CanUsePoint(IProperty<Point> property) => IsStatic(property)
            || allowKeyframe && !property.HasExpression
                && property.Animation is KeyFrameAnimation<Point> { KeyFrames.Count: 1 } animation
                && animation.KeyFrames[0] is KeyFrame<Point>;
        if (index == figure.Segments.Count)
            return CanUsePoint(figure.StartPoint) && !ReadPoint(figure.StartPoint).Value.IsInvalid
                && IsStatic(figure.IsClosed) && figure.IsClosed.CurrentValue
                && CanUsePoint(figure.Segments[^1].GetEndPoint());
        // Only this edge's actual start point affects its split. Animation on an
        // unrelated anchor (or on an unused explicit start) must not block insertion.
        bool supportedStart = index > 0 ? CanUsePoint(figure.Segments[index - 1].GetEndPoint())
            : CanUsePoint(figure.StartPoint) && (!ReadPoint(figure.StartPoint).Value.IsInvalid
                || IsStatic(figure.IsClosed) && figure.IsClosed.CurrentValue
                    && CanUsePoint(figure.Segments[^1].GetEndPoint()));
        var segment = figure.Segments[index];
        return supportedStart && segment is LineSegment or CubicBezierSegment or QuadraticBezierSegment or ConicSegment or ArcSegment
            && CanUsePoint(segment.GetEndPoint())
            && PathEditorHelper.GetControlPointProperties(segment).All(CanUsePoint)
            && (segment is not ConicSegment c || IsStatic(c.Weight) && float.IsFinite(c.Weight.CurrentValue) && c.Weight.CurrentValue >= 0)
            && (segment is not ArcSegment arc || IsStatic(arc.Radius) && IsStatic(arc.RotationAngle)
                && IsStatic(arc.IsLargeArc) && IsStatic(arc.SweepClockwise));
    }

    internal static PathSegment? Split(PathFigure figure, int index, float t, CompositionContext context)
    {
        if (t <= 0 || t >= 1 || !CanSplit(figure, index)) return null;
        SplitPoint startPoint = ReadPoint(index > 0 ? figure.Segments[index - 1].GetEndPoint()
            : !ReadPoint(figure.StartPoint).Value.IsInvalid ? figure.StartPoint : figure.Segments[^1].GetEndPoint());
        if (index == figure.Segments.Count)
        {
            var closingPoint = new LineSegment();
            WritePoint(closingPoint.Point, Blend(startPoint, ReadPoint(figure.StartPoint), t));
            figure.Segments.Add(closingPoint);
            return closingPoint;
        }
        PathSegment segment = figure.Segments[index];
        Point start = Start(figure, index, context);
        SplitPoint endPoint = ReadPoint(segment.GetEndPoint());
        PathSegment inserted;
        if (segment is CubicBezierSegment c)
        {
            SplitPoint control1 = ReadPoint(c.ControlPoint1), control2 = ReadPoint(c.ControlPoint2);
            SplitPoint a = Blend(startPoint, control1, t), b = Blend(control1, control2, t);
            SplitPoint d = Blend(control2, endPoint, t);
            SplitPoint e = Blend(a, b, t), f = Blend(b, d, t), point = Blend(e, f, t);
            var part = new CubicBezierSegment();
            WritePoint(part.ControlPoint1, a);
            WritePoint(part.ControlPoint2, e);
            WritePoint(part.EndPoint, point);
            inserted = part;
            WritePoint(c.ControlPoint1, f);
            WritePoint(c.ControlPoint2, d);
        }
        else if (segment is QuadraticBezierSegment q)
        {
            SplitPoint control = ReadPoint(q.ControlPoint);
            SplitPoint a = Blend(startPoint, control, t), b = Blend(control, endPoint, t);
            var part = new QuadraticBezierSegment();
            WritePoint(part.ControlPoint, a);
            WritePoint(part.EndPoint, Blend(a, b, t));
            inserted = part;
            WritePoint(q.ControlPoint, b);
        }
        else if (segment is ConicSegment conic)
        {
            float w = conic.Weight.CurrentValue, u = 1 - t;
            float wa = u + w * t, wb = w * u + t, wm = u * wa + t * wb;
            SplitPoint control = ReadPoint(conic.ControlPoint);
            SplitPoint a = Weighted(startPoint, u, control, w * t);
            SplitPoint b = Weighted(control, w * u, endPoint, t);
            SplitPoint point = Weighted(a, u * wa, b, t * wb);
            var part = new ConicSegment { Weight = { CurrentValue = wa / MathF.Sqrt(wm) } };
            WritePoint(part.ControlPoint, a);
            WritePoint(part.EndPoint, point);
            inserted = part;
            WritePoint(conic.ControlPoint, b);
            conic.Weight.CurrentValue = wb / MathF.Sqrt(wm);
        }
        else if (segment is ArcSegment arc && GetArc(start, arc, context) is { } ellipse)
        {
            // Retain elliptical arcs and their effective radii; converting to a
            // cubic approximation would change the shape at each insertion.
            Size radius = new((float)ellipse.RadiusX, (float)ellipse.RadiusY);
            inserted = new ArcSegment
            {
                Radius = { CurrentValue = radius },
                RotationAngle = { CurrentValue = arc.RotationAngle.CurrentValue },
                IsLargeArc = { CurrentValue = Math.Abs(ellipse.Sweep * t) > Math.PI },
                SweepClockwise = { CurrentValue = arc.SweepClockwise.CurrentValue },
                Point = { CurrentValue = ellipse.At(t) }
            };
            arc.Radius.CurrentValue = radius;
            arc.IsLargeArc.CurrentValue = Math.Abs(ellipse.Sweep * (1 - t)) > Math.PI;
        }
        else
        {
            var part = new LineSegment();
            WritePoint(part.Point, Blend(startPoint, endPoint, t));
            inserted = part;
        }
        figure.Segments.Insert(index, inserted);
        return inserted;
    }

    private static Point Weighted(Point a, float wa, Point b, float wb) =>
        new((a.X * wa + b.X * wb) / (wa + wb), (a.Y * wa + b.Y * wb) / (wa + wb));

    private readonly record struct SplitPoint(Point BaseValue, Point Value, KeyFrameAnimation<Point>? Animation);

    private static SplitPoint ReadPoint(IProperty<Point> property)
    {
        var animation = property.Animation as KeyFrameAnimation<Point>;
        return new(property.CurrentValue, animation?.KeyFrames[0] is KeyFrame<Point> key
            ? key.Value : property.CurrentValue, animation);
    }

    private static SplitPoint Blend(SplitPoint a, SplitPoint b, float t) =>
        new(Lerp(a.BaseValue, b.BaseValue, t), Lerp(a.Value, b.Value, t), a.Animation ?? b.Animation);

    private static SplitPoint Weighted(SplitPoint a, float wa, SplitPoint b, float wb) =>
        new(Weighted(a.BaseValue, wa, b.BaseValue, wb), Weighted(a.Value, wa, b.Value, wb), a.Animation ?? b.Animation);

    private static void WritePoint(IProperty<Point> property, SplitPoint point)
    {
        property.CurrentValue = point.BaseValue;
        if (point.Animation == null) return;
        if (property.Animation is KeyFrameAnimation<Point> { KeyFrames.Count: 1 } existing
            && existing.KeyFrames[0] is KeyFrame<Point> key)
        {
            key.Value = point.Value;
            return;
        }

        // Copy metadata/easing without sharing mutable keyframes or duplicating IDs.
        var json = CoreSerializer.SerializeToJsonObject((KeyFrame<Point>)point.Animation.KeyFrames[0]);
        json.Remove(nameof(CoreObject.Id));
        var derived = (KeyFrame<Point>)CoreSerializer.DeserializeFromJsonObject(json, typeof(KeyFrame<Point>));
        derived.Value = point.Value;
        var animation = new KeyFrameAnimation<Point>
        {
            UseGlobalClock = point.Animation.UseGlobalClock,
            Name = point.Animation.Name
        };
        animation.KeyFrames.Add(derived);
        property.Animation = animation;
    }

    internal static CubicBezierSegment Cubic(Point c1, Point c2, Point end) => new()
    {
        ControlPoint1 = { CurrentValue = c1 },
        ControlPoint2 = { CurrentValue = c2 },
        EndPoint = { CurrentValue = end }
    };

    internal static CubicBezierSegment? ToCubic(PathFigure figure, int index, CompositionContext context)
    {
        if (!CanSplit(figure, index, preserveSingleKeyframe: false)) return null;
        if (index == figure.Segments.Count)
        {
            Point last = Start(figure, index, context), first = figure.StartPoint.GetValue(context);
            var closing = Cubic(Lerp(last, first, 1f / 3), Lerp(last, first, 2f / 3), first);
            figure.Segments.Add(closing);
            return closing;
        }
        if (figure.Segments[index] is not (LineSegment or CubicBezierSegment or QuadraticBezierSegment)) return null;
        if (figure.Segments[index] is CubicBezierSegment cubic) return cubic;
        Point start = Start(figure, index, context), end = figure.Segments[index].GetEndPoint().CurrentValue;
        cubic = figure.Segments[index] is QuadraticBezierSegment q
            ? Cubic(Lerp(start, q.ControlPoint.CurrentValue, 2f / 3), Lerp(end, q.ControlPoint.CurrentValue, 2f / 3), end)
            : Cubic(Lerp(start, end, 1f / 3), Lerp(start, end, 2f / 3), end);
        figure.Segments[index] = cubic;
        return cubic;
    }

    private readonly record struct EllipticalArc(double CenterX, double CenterY, double RadiusX,
        double RadiusY, double SinRotation, double CosRotation, double Angle, double Sweep)
    {
        public Point At(float t)
        {
            double angle = Angle + Sweep * t;
            double x = RadiusX * Math.Cos(angle), y = RadiusY * Math.Sin(angle);
            return new((float)(CenterX + x * CosRotation - y * SinRotation),
                (float)(CenterY + x * SinRotation + y * CosRotation));
        }
    }

    // Use the renderer's float arithmetic for SVG radius correction and the center.
    // A double-precision center can differ visibly at a nearly semicircular arc.
    private static EllipticalArc? GetArc(Point start, ArcSegment arc, CompositionContext context)
    {
        Point end = arc.Point.GetValue(context);
        Size radius = arc.Radius.GetValue(context);
        float rx = Math.Abs(radius.Width), ry = Math.Abs(radius.Height);
        if (start == end || rx == 0 || ry == 0) return null;
        float rotation = arc.RotationAngle.GetValue(context);
        SKMatrix unrotate = SKMatrix.CreateRotationDegrees(-rotation);
        var midpoint = unrotate.MapPoint((start.X - end.X) / 2, (start.Y - end.Y) / 2);
        float scale = midpoint.X * midpoint.X / (rx * rx) + midpoint.Y * midpoint.Y / (ry * ry);
        if (scale > 1) { rx *= MathF.Sqrt(scale); ry *= MathF.Sqrt(scale); }
        SKMatrix normalize = SKMatrix.Concat(SKMatrix.CreateScale(1 / rx, 1 / ry), unrotate);
        SKPoint a = normalize.MapPoint(start.X, start.Y), b = normalize.MapPoint(end.X, end.Y);
        float dx = b.X - a.X, dy = b.Y - a.Y;
        bool clockwise = arc.SweepClockwise.GetValue(context);
        float factor = MathF.Sqrt(Math.Max(0, 1 / (dx * dx + dy * dy) - .25f));
        if (!clockwise != arc.IsLargeArc.GetValue(context)) factor = -factor;
        float cx = (a.X + b.X) / 2 - dy * factor, cy = (a.Y + b.Y) / 2 + dx * factor;
        float angle = MathF.Atan2(a.Y - cy, a.X - cx);
        float sweep = MathF.Atan2(b.Y - cy, b.X - cx) - angle;
        if (clockwise && sweep < 0) sweep += 2 * MathF.PI;
        else if (!clockwise && sweep > 0) sweep -= 2 * MathF.PI;
        SKMatrix rotate = SKMatrix.CreateRotationDegrees(rotation);
        var center = SKMatrix.Concat(rotate, SKMatrix.CreateScale(rx, ry)).MapPoint(cx, cy);
        return new(center.X, center.Y, rx, ry, rotate.SkewY, rotate.ScaleX, angle, sweep);
    }
}
