#pragma warning disable CS0618 // Type or member is obsolete

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Xaml.Interactivity;
using Beutl.Composition;
using Beutl.Controls;
using Beutl.Editor.Components.PathEditorTab.Services;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Media;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using BtlPoint = Beutl.Graphics.Point;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.PathEditorTab.Views;

public static class PathEditorHelper
{
    internal static void UpdateThumbPositions(Panel canvas, IPathEditorView view, CompositionContext context)
    {
        foreach (Thumb thumb in canvas.Children.OfType<Thumb>())
        {
            if (GetProperty(thumb) is { } property)
                SetCanvasPosition(thumb, view.Matrix.Transform(property.GetValue(context).ToAvaPoint()) * view.Scale);
        }
    }

    // The dedicated path tab and the preview overlay share the thumb canvas below.
    internal static void RemoveAllThumbs(Panel canvas)
    {
        canvas.Children.RemoveAll(canvas.Children
            .Where(c => c is Thumb)
            .Do(t => t.DataContext = null));
    }

    internal static void RemoveSegmentThumbs(Panel canvas, PathSegment segment)
    {
        canvas.Children.RemoveAll(canvas.Children
            .Where(c => c is Thumb t && t.DataContext == segment)
            .Do(t => t.DataContext = null));
    }

    internal static void ReplaceSegmentThumbs(
        Panel canvas,
        PathFigure? figure,
        ref IDisposable? subscription,
        Action<int, PathSegment> attached,
        Action<int, PathSegment> detached)
    {
        RemoveAllThumbs(canvas);

        subscription?.Dispose();
        subscription = figure?.Segments.ForEachItem(
            attached,
            detached,
            () => RemoveAllThumbs(canvas));
    }

    internal static Thumb CreateEditorThumb(EventHandler<RoutedEventArgs> onDelete)
    {
        var thumb = new Thumb()
        {
            [!Avalonia.StyledElement.ThemeProperty] = new DynamicResourceExtension("PathEditorControlPointThumbTheme")
        };
        var flyout = new FAMenuFlyout();
        var delete = new FAMenuFlyoutItem
        {
            Text = Strings.Delete,
            IconSource = new FluentIconSource { Icon = Icon.Delete }
        };
        delete.Click += onDelete;
        flyout.ItemsSource = new[] { delete };

        thumb.ContextFlyout = flyout;

        Interaction.GetBehaviors(thumb).Add(new PathPointDragBehavior());

        return thumb;
    }

    internal static void DeleteSegment(object? sender, IPathEditorContext? context)
    {
        if (sender is FAMenuFlyoutItem { DataContext: PathSegment op }
            && context?.FigureContext.Value is IPathFigureEditorContext figureContext)
        {
            int index = figureContext.GetSegmentIndex(op);
            if (index >= 0)
                figureContext.RemoveSegment(index);
        }
    }

    internal static void AddSegmentAt(
        IPathEditorContext context, object? tag, Avalonia.Point click, double scale, Avalonia.Matrix matrix)
    {
        if (context.PathFigure.Value is { } figure
            && context.FigureContext.Value is IPathFigureEditorContext figureContext)
        {
            var clock = context.EditorContext.GetRequiredService<IEditorClock>();
            int index = figure.Segments.Count;
            BtlPoint lastPoint = default;
            if (index > 0)
            {
                PathSegment lastOp = figure.Segments[index - 1];
                lastPoint = lastOp.GetEndPoint().GetValue(new CompositionContext(clock.CurrentTime.Value));
            }

            BtlPoint point = (click / scale).ToBtlPoint();
            if (matrix.TryInvert(out Avalonia.Matrix mat))
            {
                point = mat.ToBtlMatrix().Transform(point);
            }

            PathSegment? obj = CreateSegment(tag, point, lastPoint);

            if (obj != null)
            {
                figureContext.AddSegment(obj);
            }
        }
    }

    internal static void ApplyDragMode(IPathEditorContext context, object? tag)
    {
        context.Symmetry.Value = false;
        context.Asymmetry.Value = false;
        context.Separately.Value = false;

        switch (tag)
        {
            case "Symmetry":
                context.Symmetry.Value = true;
                break;
            case "Asymmetry":
                context.Asymmetry.Value = true;
                break;
            case "Separately":
                context.Separately.Value = true;
                break;
        }
    }

    internal static Thumb? FindThumb(Panel canvas, PathSegment segment, IProperty<BtlPoint> property)
    {
        return canvas.Children.FirstOrDefault(v =>
            ReferenceEquals(v.DataContext, segment) && Equals(v.Tag, property.Name)) as Thumb;
    }

    internal static Thumb[] GetSelectedAnchors(Panel canvas)
    {
        return canvas.Children.OfType<Thumb>()
            .Where(c => !c.Classes.Contains("control") && PathPointDragBehavior.GetIsSelected(c))
            .ToArray();
    }

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
