#pragma warning disable CS0618 // Type or member is obsolete

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Beutl.Engine;
using Beutl.Media;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Editor.Components.PathEditorTab.Views;

public static class PathEditorHelper
{
    public static IProperty<BtlPoint>[] GetControlPointProperties(object datacontext)
    {
        return datacontext switch
        {
            ConicSegment conicSegment => [conicSegment.ControlPoint],
            CubicBezierSegment cubicBezierSegment =>
                [cubicBezierSegment.ControlPoint1, cubicBezierSegment.ControlPoint2],
            QuadraticBezierSegment quadraticBezierSegment => [quadraticBezierSegment.ControlPoint],
            _ => [],
        };
    }

    public static IProperty<BtlPoint>? GetControlPointProperty(object datacontext, int i)
    {
        return datacontext switch
        {
            ConicSegment conicSegment => conicSegment.ControlPoint,
            CubicBezierSegment cubicBezierSegment => i == 0
                ? cubicBezierSegment.ControlPoint1
                : cubicBezierSegment.ControlPoint2,
            QuadraticBezierSegment quadraticBezierSegment => quadraticBezierSegment.ControlPoint,
            _ => null,
        };
    }

    public static IProperty<BtlPoint>? GetProperty(Thumb t)
    {
        switch (t.DataContext)
        {
            case ArcSegment arcSegment:
                return arcSegment.Point;

            case ConicSegment conicSegment:
                switch (t.Tag)
                {
                    case "ControlPoint":
                        return conicSegment.ControlPoint;
                    case "EndPoint":
                        return conicSegment.EndPoint;
                }

                break;

            case CubicBezierSegment cubicBezierSegment:
                switch (t.Tag)
                {
                    case "ControlPoint1":
                        return cubicBezierSegment.ControlPoint1;

                    case "ControlPoint2":
                        return cubicBezierSegment.ControlPoint2;
                    case "EndPoint":
                        return cubicBezierSegment.EndPoint;
                }

                break;

            case LineSegment lineSegment:
                return lineSegment.Point;

            case QuadraticBezierSegment quadraticBezierSegment:
                switch (t.Tag)
                {
                    case "ControlPoint":
                        return quadraticBezierSegment.ControlPoint;
                    case "EndPoint":
                        return quadraticBezierSegment.EndPoint;
                }

                break;
        }

        return null;
    }

    public static PathSegment? CreateSegment(object? tag, BtlPoint point, BtlPoint lastPoint)
    {
        return tag switch
        {
            "Arc" => new ArcSegment() { Point = { CurrentValue = point } },
            "Conic" => new ConicSegment()
            {
                EndPoint = { CurrentValue = point },
                ControlPoint =
                {
                    CurrentValue = new(float.Lerp(point.X, lastPoint.X, 0.5f),
                        float.Lerp(point.Y, lastPoint.Y, 0.5f))
                }
            },
            "Cubic" => new CubicBezierSegment()
            {
                EndPoint = { CurrentValue = point },
                ControlPoint1 =
                {
                    CurrentValue = new(float.Lerp(point.X, lastPoint.X, 0.66f),
                        float.Lerp(point.Y, lastPoint.Y, 0.66f))
                },
                ControlPoint2 =
                {
                    CurrentValue = new(float.Lerp(point.X, lastPoint.X, 0.33f),
                        float.Lerp(point.Y, lastPoint.Y, 0.33f))
                },
            },
            "Line" => new LineSegment() { Point = { CurrentValue = point } },
            "Quad" => new QuadraticBezierSegment()
            {
                EndPoint = { CurrentValue = point },
                ControlPoint =
                {
                    CurrentValue = new(float.Lerp(point.X, lastPoint.X, 0.5f),
                        float.Lerp(point.Y, lastPoint.Y, 0.5f))
                }
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
