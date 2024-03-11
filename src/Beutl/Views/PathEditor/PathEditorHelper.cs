#pragma warning disable CS0618 // Type or member is obsolete

using Avalonia.Controls;
using Avalonia.Controls.Primitives;

using Beutl.Media;

using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Views;

public static class PathEditorHelper
{
    public static CoreProperty<BtlPoint>[] GetControlPointProperties(object datacontext)
    {
        return datacontext switch
        {
            ConicSegment => [ConicSegment.ControlPointProperty],
            CubicBezierSegment => [CubicBezierSegment.ControlPoint1Property, CubicBezierSegment.ControlPoint2Property],
            QuadraticBezierSegment => [QuadraticBezierSegment.ControlPointProperty],
            _ => [],
        };
    }

    public static CoreProperty<BtlPoint>? GetControlPointProperty(object datacontext, int i)
    {
        return datacontext switch
        {
            ConicSegment => ConicSegment.ControlPointProperty,
            CubicBezierSegment => i == 0 ? CubicBezierSegment.ControlPoint1Property : CubicBezierSegment.ControlPoint2Property,
            QuadraticBezierSegment => QuadraticBezierSegment.ControlPointProperty,
            _ => null,
        };
    }

    public static CoreProperty<BtlPoint>? GetProperty(Thumb t)
    {
        switch (t.DataContext)
        {
            case ArcSegment:
                return ArcSegment.PointProperty;

            case ConicSegment:
                switch (t.Tag)
                {
                    case "ControlPoint":
                        return ConicSegment.ControlPointProperty;
                    case "EndPoint":
                        return ConicSegment.EndPointProperty;
                }
                break;

            case CubicBezierSegment:
                switch (t.Tag)
                {
                    case "ControlPoint1":
                        return CubicBezierSegment.ControlPoint1Property;

                    case "ControlPoint2":
                        return CubicBezierSegment.ControlPoint2Property;
                    case "EndPoint":
                        return CubicBezierSegment.EndPointProperty;
                }
                break;

            case LineSegment:
                return LineSegment.PointProperty;

            case MoveOperation:
                return MoveOperation.PointProperty;

            case QuadraticBezierSegment:
                switch (t.Tag)
                {
                    case "ControlPoint":
                        return QuadraticBezierSegment.ControlPointProperty;
                    case "EndPoint":
                        return QuadraticBezierSegment.EndPointProperty;
                }
                break;
        }

        return null;
    }

    public static PathSegment? CreateSegment(object? tag, BtlPoint point, BtlPoint lastPoint)
    {
        return tag switch
        {
            "Arc" => new ArcSegment() { Point = point },
            "Conic" => new ConicSegment()
            {
                EndPoint = point,
                ControlPoint = new(float.Lerp(point.X, lastPoint.X, 0.5f), float.Lerp(point.Y, lastPoint.Y, 0.5f))
            },
            "Cubic" => new CubicBezierSegment()
            {
                EndPoint = point,
                ControlPoint1 = new(float.Lerp(point.X, lastPoint.X, 0.66f), float.Lerp(point.Y, lastPoint.Y, 0.66f)),
                ControlPoint2 = new(float.Lerp(point.X, lastPoint.X, 0.33f), float.Lerp(point.Y, lastPoint.Y, 0.33f)),
            },
            "Line" => new LineSegment() { Point = point },
            "Quad" => new QuadraticBezierSegment()
            {
                EndPoint = point,
                ControlPoint = new(float.Lerp(point.X, lastPoint.X, 0.5f), float.Lerp(point.Y, lastPoint.Y, 0.5f))
            },
            _ => null,
        };
    }

    public static Thumb[] CreateThumbs(PathSegment obj, Func<Thumb> create)
    {
        switch (obj)
        {
            case ArcSegment:
                {
                    Thumb t = create();
                    t.DataContext = obj;

                    return [t];
                }

            case ConicSegment:
                {
                    Thumb c1 = create();
                    c1.Tag = "ControlPoint";
                    c1.Classes.Add("control");
                    c1.DataContext = obj;

                    Thumb e = create();
                    e.Tag = "EndPoint";
                    e.DataContext = obj;

                    return [e, c1];
                }

            case CubicBezierSegment:
                {
                    Thumb c1 = create();
                    c1.Classes.Add("control");
                    c1.Tag = "ControlPoint1";
                    c1.DataContext = obj;

                    Thumb c2 = create();
                    c2.Classes.Add("control");
                    c2.Tag = "ControlPoint2";
                    c2.DataContext = obj;

                    Thumb e = create();
                    e.Tag = "EndPoint";
                    e.DataContext = obj;

                    return [e, c2, c1];
                }

            case LineSegment:
            case MoveOperation:
                {
                    Thumb t = create();
                    t.DataContext = obj;

                    return [t];
                }

            case QuadraticBezierSegment:
                {
                    Thumb c1 = create();
                    c1.Tag = "ControlPoint";
                    c1.Classes.Add("control");
                    c1.DataContext = obj;

                    Thumb e = create();
                    e.Tag = "EndPoint";
                    e.DataContext = obj;

                    return [e, c1];
                }

            default:
                return [];
        }
    }

    public static double Round(double v)
    {
        return Math.Round(v, 2, MidpointRounding.AwayFromZero);
    }

    public static float Round(float v)
    {
        return MathF.Round(v, 2, MidpointRounding.AwayFromZero);
    }

    public static Avalonia.Point Round(Avalonia.Point p)
    {
        return new(Round(p.X), Round(p.Y));
    }

    public static Avalonia.Point Round(Avalonia.Point p, Avalonia.Matrix m)
    {
        return Round(p.Transform(m.Invert())).Transform(m);
    }

    public static BtlPoint Round(BtlPoint p)
    {
        return new(Round(p.X), Round(p.Y));
    }

    public static Avalonia.Point GetCanvasPosition(Control c)
    {
        return new(Canvas.GetLeft(c), Canvas.GetTop(c));
    }

    public static void SetCanvasPosition(Control c, Avalonia.Point p)
    {
        Canvas.SetLeft(c, p.X);
        Canvas.SetTop(c, p.Y);
    }
}
