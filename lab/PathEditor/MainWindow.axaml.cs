using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

using Beutl;
using Beutl.Collections;
using Beutl.Media;

namespace PathEditor;

public partial class MainWindow : Window
{
    private readonly PathGeometry _geometry = new();
    private Point _clickPoint;

    public MainWindow()
    {
        InitializeComponent();
        canvas.Children.Add(new PathGeometryControl(_geometry));
        canvas.AddHandler(PointerPressedEvent, OnCanvasPointerPressed, RoutingStrategies.Tunnel);

        _geometry.Segments.ForEachItem(
            OnOperationAttached,
            OnOperationDetached,
            () => canvas.Children.RemoveAll(canvas.Children
                    .Where(c => c is Thumb)
                    .Do(t => ((Thumb)t).DataContext = null)));
    }

    private void OnOperationDetached(int index, PathSegment obj)
    {
        canvas.Children.RemoveAll(canvas.Children
            .Where(c => c is Thumb t && t.DataContext == obj)
            .Do(t => ((Thumb)t).DataContext = null));
    }

    private static IObservable<Beutl.Graphics.Point> GetObservable(Thumb obj, CoreProperty<Beutl.Graphics.Point> p)
    {
        return obj.GetObservable(DataContextProperty)
            .Select(v => (v as PathSegment)?.GetObservable(p) ?? Observable.Return((Beutl.Graphics.Point)default))
            .Switch();
    }

    private void OnOperationAttached(int index, PathSegment obj)
    {
        switch (obj)
        {
            case ArcSegment arc:
                {
                    Thumb t = CreateThumb();
                    t.DataContext = obj;
                    t.Bind(Canvas.LeftProperty, GetObservable(t, ArcSegment.PointProperty).Select(v => v.X).ToBinding());
                    t.Bind(Canvas.TopProperty, GetObservable(t, ArcSegment.PointProperty).Select(v => v.Y).ToBinding());

                    canvas.Children.Add(t);
                }
                break;

            case CubicBezierSegment cubic:
                {
                    Thumb c1 = CreateThumb();
                    c1.Classes.Add("control");
                    c1.Tag = "ControlPoint1";
                    c1.DataContext = obj;
                    c1.Bind(Canvas.LeftProperty, GetObservable(c1, CubicBezierSegment.ControlPoint1Property).Select(v => v.X).ToBinding());
                    c1.Bind(Canvas.TopProperty, GetObservable(c1, CubicBezierSegment.ControlPoint1Property).Select(v => v.Y).ToBinding());

                    Thumb c2 = CreateThumb();
                    c2.Classes.Add("control");
                    c2.Tag = "ControlPoint2";
                    c2.DataContext = obj;
                    c2.Bind(Canvas.LeftProperty, GetObservable(c2, CubicBezierSegment.ControlPoint2Property).Select(v => v.X).ToBinding());
                    c2.Bind(Canvas.TopProperty, GetObservable(c2, CubicBezierSegment.ControlPoint2Property).Select(v => v.Y).ToBinding());

                    Thumb e = CreateThumb();
                    e.Tag = "EndPoint";
                    e.DataContext = obj;
                    e.Bind(Canvas.LeftProperty, GetObservable(e, CubicBezierSegment.EndPointProperty).Select(v => v.X).ToBinding());
                    e.Bind(Canvas.TopProperty, GetObservable(e, CubicBezierSegment.EndPointProperty).Select(v => v.Y).ToBinding());

                    canvas.Children.Add(e);
                    canvas.Children.Add(c2);
                    canvas.Children.Add(c1);
                }
                break;

            case LineSegment line:
                {
                    Thumb t = CreateThumb();
                    t.DataContext = obj;
                    t.Bind(Canvas.LeftProperty, GetObservable(t, LineSegment.PointProperty).Select(v => v.X).ToBinding());
                    t.Bind(Canvas.TopProperty, GetObservable(t, LineSegment.PointProperty).Select(v => v.Y).ToBinding());

                    canvas.Children.Add(t);
                }
                break;

            case MoveOperation move:
                {
                    Thumb t = CreateThumb();
                    t.DataContext = obj;
                    t.Bind(Canvas.LeftProperty, GetObservable(t, MoveOperation.PointProperty).Select(v => v.X).ToBinding());
                    t.Bind(Canvas.TopProperty, GetObservable(t, MoveOperation.PointProperty).Select(v => v.Y).ToBinding());

                    canvas.Children.Add(t);
                }
                break;

            case QuadraticBezierSegment quad:
                {
                    Thumb c1 = CreateThumb();
                    c1.Tag = "ControlPoint";
                    c1.Classes.Add("control");
                    c1.DataContext = obj;
                    c1.Bind(Canvas.LeftProperty, GetObservable(c1, QuadraticBezierSegment.ControlPointProperty).Select(v => v.X).ToBinding());
                    c1.Bind(Canvas.TopProperty, GetObservable(c1, QuadraticBezierSegment.ControlPointProperty).Select(v => v.Y).ToBinding());

                    Thumb e = CreateThumb();
                    e.Tag = "EndPoint";
                    e.DataContext = obj;
                    e.Bind(Canvas.LeftProperty, GetObservable(e, QuadraticBezierSegment.EndPointProperty).Select(v => v.X).ToBinding());
                    e.Bind(Canvas.TopProperty, GetObservable(e, QuadraticBezierSegment.EndPointProperty).Select(v => v.Y).ToBinding());

                    canvas.Children.Add(e);
                    canvas.Children.Add(c1);
                }
                break;

            case CloseOperation:
            default:
                break;
        }
    }

    private Thumb CreateThumb()
    {
        //ControlPointThumb
        var thumb = new Thumb()
        {
            Theme = this.FindResource("ControlPointThumb") as ControlTheme
        };
        thumb.DragDelta += OnThumbDragDelta;

        return thumb;
    }

    private void OnThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (sender is Thumb t)
        {
            var delta = new Beutl.Graphics.Vector((float)e.Vector.X, (float)e.Vector.Y);
            switch (t.DataContext)
            {
                case ArcSegment arc:
                    arc.Point += delta;
                    break;

                case CubicBezierSegment cubic:
                    switch (t.Tag)
                    {
                        case "ControlPoint1":
                            cubic.ControlPoint1 += delta;
                            break;
                        case "ControlPoint2":
                            cubic.ControlPoint2 += delta;
                            break;
                        case "EndPoint":
                            cubic.EndPoint += delta;
                            break;
                    }
                    break;

                case LineSegment line:
                    line.Point += delta;
                    break;

                case MoveOperation move:
                    move.Point += delta;
                    break;

                case QuadraticBezierSegment quad:
                    switch (t.Tag)
                    {
                        case "ControlPoint":
                            quad.ControlPoint += delta;
                            break;
                        case "EndPoint":
                            quad.EndPoint += delta;
                            break;
                    }
                    break;

                case CloseOperation:
                default:
                    break;
            }
        }
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint pt = e.GetCurrentPoint(canvas);
        if (pt.Properties.IsRightButtonPressed)
        {
            _clickPoint = pt.Position;
        }
    }

    private void AddOpClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            int index = _geometry.Segments.Count;
            Beutl.Graphics.Point lastPoint = default;
            if (index > 0)
            {
                PathSegment lastOp = _geometry.Segments[index - 1];
                lastPoint = lastOp switch
                {
                    ArcSegment arc => arc.Point,
                    CubicBezierSegment cub => cub.EndPoint,
                    LineSegment line => line.Point,
                    MoveOperation move => move.Point,
                    QuadraticBezierSegment quad => quad.EndPoint,
                    _ => default
                };
            }

            var point = _clickPoint.ToBtlPoint();
            PathSegment? obj = item.Header switch
            {
                "Arc" => new ArcSegment() { Point = point },
                "Close" => new CloseOperation(),
                "Cubic" => new CubicBezierSegment()
                {
                    EndPoint = point,
                    ControlPoint1 = new(float.Lerp(point.X, lastPoint.X, 0.66f), float.Lerp(point.Y, lastPoint.Y, 0.66f)),
                    ControlPoint2 = new(float.Lerp(point.X, lastPoint.X, 0.33f), float.Lerp(point.Y, lastPoint.Y, 0.33f)),
                },
                "Line" => new LineSegment() { Point = point },
                "Move" => new MoveOperation() { Point = point },
                "Quad" => new QuadraticBezierSegment()
                {
                    EndPoint = point,
                    ControlPoint = new(float.Lerp(point.X, lastPoint.X, 0.5f), float.Lerp(point.Y, lastPoint.Y, 0.5f))
                },
                _ => null,
            };

            if (obj != null)
            {

                _geometry.Segments.Add(obj);
            }
        }
    }
}
