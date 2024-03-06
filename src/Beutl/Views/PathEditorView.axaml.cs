using System.Security.Cryptography.Xml;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;

using Beutl.Media;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;

namespace Beutl.Views;

public partial class PathEditorView : UserControl
{
    public static readonly StyledProperty<int> SceneWidthProperty =
        AvaloniaProperty.Register<PathEditorView, int>(nameof(SceneWidth));

    public static readonly DirectProperty<PathEditorView, double> ScaleProperty =
        AvaloniaProperty.RegisterDirect<PathEditorView, double>(nameof(Scale),
            o => o.Scale);

    public static readonly StyledProperty<Matrix> MatrixProperty =
        AvaloniaProperty.Register<PathEditorView, Matrix>(nameof(Matrix), Matrix.Identity);

    private double _scale = 1;
    private Point _clickPoint;
    private IDisposable? _disposable;

    public PathEditorView()
    {
        InitializeComponent();
        canvas.AddHandler(PointerPressedEvent, OnCanvasPointerPressed, RoutingStrategies.Tunnel);

        view.GetObservable(PathGeometryControl.GeometryProperty)
            .Subscribe(geo =>
            {
                canvas.Children.RemoveAll(canvas.Children
                    .Where(c => c is Thumb)
                    .Do(t => t.DataContext = null));

                _disposable?.Dispose();
                _disposable = geo?.Operations.ForEachItem(
                    OnOperationAttached,
                    OnOperationDetached,
                    () => canvas.Children.RemoveAll(canvas.Children
                        .Where(c => c is Thumb)
                        .Do(t => t.DataContext = null)));
            });
    }

    public int SceneWidth
    {
        get => GetValue(SceneWidthProperty);
        set => SetValue(SceneWidthProperty, value);
    }

    public Matrix Matrix
    {
        get => GetValue(MatrixProperty);
        set => SetValue(MatrixProperty, value);
    }

    public double Scale
    {
        get => _scale;
        private set => SetAndRaise(ScaleProperty, ref _scale, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneWidthProperty || change.Property == BoundsProperty)
        {
            if (SceneWidth != 0)
            {
                Scale = Bounds.Width / SceneWidth;
            }
            else
            {
                Scale = 1;
            }
        }
    }

    private void OnOperationDetached(int index, PathOperation obj)
    {
        canvas.Children.RemoveAll(canvas.Children
            .Where(c => c is Thumb t && t.DataContext == obj)
            .Do(t => t.DataContext = null));
    }

    private static IObservable<Point> GetObservable(Thumb obj, CoreProperty<Graphics.Point> p)
    {
        return obj.GetObservable(DataContextProperty)
            .Select(v => (v as PathOperation)?.GetObservable(p) ?? Observable.Return((Graphics.Point)default))
            .Switch()
            .Select(v => v.ToAvaPoint());
    }

    private static void Bind(Thumb t, CoreProperty<Graphics.Point> p)
    {
        var parent = ControlLocator.Track(t, 0, typeof(PathEditorView)).Select(v => v as PathEditorView);
        IObservable<double> scale = parent
            .Select(v => v?.GetObservable(ScaleProperty) ?? Observable.Return(1.0))
            .Switch();

        IObservable<Matrix> matrix = parent
            .Select(v => v?.GetObservable(MatrixProperty) ?? Observable.Return(Matrix.Identity))
            .Switch();

        IObservable<Point> point = GetObservable(t, p);

        point = point.CombineLatest(matrix)
            .Select(t => t.First.Transform(t.Second));

        t.Bind(Canvas.LeftProperty, point
            .CombineLatest(scale)
            .Select(t => t.First.X * t.Second)
            .ToBinding());

        t.Bind(Canvas.TopProperty, point
            .CombineLatest(scale)
            .Select(t => t.First.Y * t.Second)
            .ToBinding());

        if (t.Classes.Contains("control"))
        {
            t.Bind(IsVisibleProperty, parent.Select(v => v?.GetObservable(DataContextProperty) ?? Observable.Return<object?>(null))
                .Switch()
                .Select(v => (v as PathEditorViewModel)?.SelectedOperation ?? Observable.Return<PathOperation?>(null))
                .Switch()
                .CombineLatest(t.GetObservable(DataContextProperty).Select(v => v as PathOperation))
                .Select(t => t.First == t.Second));
        }
    }

    private void OnOperationAttached(int index, PathOperation obj)
    {
        switch (obj)
        {
            case ArcOperation:
                {
                    Thumb t = CreateThumb();
                    t.DataContext = obj;
                    Bind(t, ArcOperation.PointProperty);

                    canvas.Children.Add(t);
                }
                break;

            case CubicBezierOperation:
                {
                    Thumb c1 = CreateThumb();
                    c1.Classes.Add("control");
                    c1.Tag = "ControlPoint1";
                    c1.DataContext = obj;
                    Bind(c1, CubicBezierOperation.ControlPoint1Property);

                    Thumb c2 = CreateThumb();
                    c2.Classes.Add("control");
                    c2.Tag = "ControlPoint2";
                    c2.DataContext = obj;
                    Bind(c2, CubicBezierOperation.ControlPoint2Property);

                    Thumb e = CreateThumb();
                    e.Tag = "EndPoint";
                    e.DataContext = obj;
                    Bind(e, CubicBezierOperation.EndPointProperty);

                    canvas.Children.Add(e);
                    canvas.Children.Add(c2);
                    canvas.Children.Add(c1);
                }
                break;

            case LineOperation:
                {
                    Thumb t = CreateThumb();
                    t.DataContext = obj;
                    Bind(t, LineOperation.PointProperty);

                    canvas.Children.Add(t);
                }
                break;

            case MoveOperation:
                {
                    Thumb t = CreateThumb();
                    t.DataContext = obj;
                    Bind(t, MoveOperation.PointProperty);

                    canvas.Children.Add(t);
                }
                break;

            case QuadraticBezierOperation:
                {
                    Thumb c1 = CreateThumb();
                    c1.Tag = "ControlPoint";
                    c1.Classes.Add("control");
                    c1.DataContext = obj;
                    Bind(c1, QuadraticBezierOperation.ControlPointProperty);

                    Thumb e = CreateThumb();
                    e.Tag = "EndPoint";
                    e.DataContext = obj;
                    Bind(e, QuadraticBezierOperation.EndPointProperty);

                    canvas.Children.Add(e);
                    canvas.Children.Add(c1);
                }
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
        thumb.DragStarted += OnThumbDragStarted;
        thumb.DragCompleted += OnThumbDragCompleted;

        return thumb;
    }

    private void OnThumbDragCompleted(object? sender, VectorEventArgs e)
    {

    }

    private void OnThumbDragStarted(object? sender, VectorEventArgs e)
    {
        if (sender is Thumb { DataContext: PathOperation op } t
            && DataContext is PathEditorViewModel { Context.Value.Group.Value: { } group } viewModel)
        {
            foreach (ListItemEditorViewModel<PathOperation> item in group.Items)
            {
                if (item.Context is PathOperationEditorViewModel itemvm)
                {
                    if (ReferenceEquals(itemvm.Value.Value, op))
                    {
                        itemvm.IsExpanded.Value = true;
                        itemvm.ProgrammaticallyExpanded = true;
                    }
                    else if (itemvm.ProgrammaticallyExpanded)
                    {
                        itemvm.IsExpanded.Value = false;
                    }
                }
            }

            viewModel.SelectedOperation.Value = op;
        }
    }

    private void OnThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (sender is Thumb t)
        {
            var delta = new Graphics.Vector((float)(e.Vector.X / Scale), (float)(e.Vector.Y / Scale));
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
        if (sender is MenuItem item && DataContext is PathEditorViewModel { PathGeometry.Value: { } geometry })
        {
            int index = geometry.Operations.Count;
            Graphics.Point lastPoint = default;
            if (index > 0)
            {
                PathOperation lastOp = geometry.Operations[index - 1];
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

            Graphics.Point point = (_clickPoint / Scale).ToBtlPoint();
            if (Matrix.TryInvert(out var mat))
            {
                point = mat.ToBtlMatrix().Transform(point);
            }

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
                geometry.Operations.Add(obj);
            }
        }
    }
}
