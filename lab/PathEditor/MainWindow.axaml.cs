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

        _geometry.Operations.ForEachItem(
            OnOperationAttached,
            OnOperationDetached,
            () => canvas.Children.RemoveAll(canvas.Children
                    .Where(c => c is Thumb)
                    .Do(t => ((Thumb)t).DataContext = null)));
    }

    private void OnOperationDetached(int index, PathOperation obj)
    {
        canvas.Children.RemoveAll(canvas.Children
            .Where(c => c is Thumb t && t.DataContext == obj)
            .Do(t => ((Thumb)t).DataContext = null));
    }

    private static IObservable<Beutl.Graphics.Point> GetObservable(Thumb obj, CoreProperty<Beutl.Graphics.Point> p)
    {
        return obj.GetObservable(DataContextProperty)
            .Select(v => (v as PathOperation)?.GetObservable(p) ?? Observable.Return((Beutl.Graphics.Point)default))
            .Switch();
    }

    private void OnOperationAttached(int index, PathOperation obj)
    {
        switch (obj)
        {
            case ArcOperation arc:
                {
                    Thumb t = CreateThumb();
                    t.DataContext = obj;
                    t.Bind(Canvas.LeftProperty, GetObservable(t, ArcOperation.PointProperty).Select(v => v.X).ToBinding());
                    t.Bind(Canvas.TopProperty, GetObservable(t, ArcOperation.PointProperty).Select(v => v.Y).ToBinding());

                    canvas.Children.Add(t);
                }
                break;

            case CubicBezierOperation cubic:
                {
                    Thumb c1 = CreateThumb();
                    c1.Classes.Add("control");
                    c1.Tag = "ControlPoint1";
                    c1.DataContext = obj;
                    c1.Bind(Canvas.LeftProperty, GetObservable(c1, CubicBezierOperation.ControlPoint1Property).Select(v => v.X).ToBinding());
                    c1.Bind(Canvas.TopProperty, GetObservable(c1, CubicBezierOperation.ControlPoint1Property).Select(v => v.Y).ToBinding());

                    Thumb c2 = CreateThumb();
                    c2.Classes.Add("control");
                    c2.Tag = "ControlPoint2";
                    c2.DataContext = obj;
                    c2.Bind(Canvas.LeftProperty, GetObservable(c2, CubicBezierOperation.ControlPoint2Property).Select(v => v.X).ToBinding());
                    c2.Bind(Canvas.TopProperty, GetObservable(c2, CubicBezierOperation.ControlPoint2Property).Select(v => v.Y).ToBinding());

                    Thumb e = CreateThumb();
                    e.Tag = "EndPoint";
                    e.DataContext = obj;
                    e.Bind(Canvas.LeftProperty, GetObservable(e, CubicBezierOperation.EndPointProperty).Select(v => v.X).ToBinding());
                    e.Bind(Canvas.TopProperty, GetObservable(e, CubicBezierOperation.EndPointProperty).Select(v => v.Y).ToBinding());

                    canvas.Children.Add(e);
                    canvas.Children.Add(c2);
                    canvas.Children.Add(c1);
                }
                break;

            case LineOperation line:
                {
                    Thumb t = CreateThumb();
                    t.DataContext = obj;
                    t.Bind(Canvas.LeftProperty, GetObservable(t, LineOperation.PointProperty).Select(v => v.X).ToBinding());
                    t.Bind(Canvas.TopProperty, GetObservable(t, LineOperation.PointProperty).Select(v => v.Y).ToBinding());

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

            case QuadraticBezierOperation quad:
                {
                    Thumb c1 = CreateThumb();
                    c1.Tag = "ControlPoint";
                    c1.Classes.Add("control");
                    c1.DataContext = obj;
                    c1.Bind(Canvas.LeftProperty, GetObservable(c1, QuadraticBezierOperation.ControlPointProperty).Select(v => v.X).ToBinding());
                    c1.Bind(Canvas.TopProperty, GetObservable(c1, QuadraticBezierOperation.ControlPointProperty).Select(v => v.Y).ToBinding());

                    Thumb e = CreateThumb();
                    e.Tag = "EndPoint";
                    e.DataContext = obj;
                    e.Bind(Canvas.LeftProperty, GetObservable(e, QuadraticBezierOperation.EndPointProperty).Select(v => v.X).ToBinding());
                    e.Bind(Canvas.TopProperty, GetObservable(e, QuadraticBezierOperation.EndPointProperty).Select(v => v.Y).ToBinding());

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
                case ArcOperation arc:
                    arc.Point += delta;
                    break;

                case CubicBezierOperation cubic:
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

                case LineOperation line:
                    line.Point += delta;
                    break;

                case MoveOperation move:
                    move.Point += delta;
                    break;

                case QuadraticBezierOperation quad:
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
            int index = _geometry.Operations.Count;
            Beutl.Graphics.Point lastPoint = default;
            if (index > 0)
            {
                PathOperation lastOp = _geometry.Operations[index - 1];
                lastPoint = lastOp switch
                {
                    ArcOperation arc => arc.Point,
                    CubicBezierOperation cub => cub.EndPoint,
                    LineOperation line => line.Point,
                    MoveOperation move => move.Point,
                    QuadraticBezierOperation quad => quad.EndPoint,
                    _ => default
                };
            }

            var point = _clickPoint.ToBtlPoint();
            PathOperation? obj = item.Header switch
            {
                "Arc" => new ArcOperation() { Point = point },
                "Close" => new CloseOperation(),
                "Cubic" => new CubicBezierOperation()
                {
                    EndPoint = point,
                    ControlPoint1 = new(float.Lerp(point.X, lastPoint.X, 0.66f), float.Lerp(point.Y, lastPoint.Y, 0.66f)),
                    ControlPoint2 = new(float.Lerp(point.X, lastPoint.X, 0.33f), float.Lerp(point.Y, lastPoint.Y, 0.33f)),
                },
                "Line" => new LineOperation() { Point = point },
                "Move" => new MoveOperation() { Point = point },
                "Quad" => new QuadraticBezierOperation()
                {
                    EndPoint = point,
                    ControlPoint = new(float.Lerp(point.X, lastPoint.X, 0.5f), float.Lerp(point.Y, lastPoint.Y, 0.5f))
                },
                _ => null,
            };

            if (obj != null)
            {

                _geometry.Operations.Add(obj);
            }
        }
    }
}
