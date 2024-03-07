#pragma warning disable CS0618

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.ReactiveUI;
using Avalonia.Styling;

using Beutl.Media;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;

using FluentAvalonia.UI.Controls;

using Reactive.Bindings.Extensions;

using ReactiveUI;

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
                _disposable = geo?.Segments.ForEachItem(
                    OnOperationAttached,
                    OnOperationDetached,
                    () => canvas.Children.RemoveAll(canvas.Children
                        .Where(c => c is Thumb)
                        .Do(t => t.DataContext = null)));
            });

        // 選択されているアンカーまたは、PathGeometry.IsClosedが変更されたとき、
        // アンカーの可視性を変更する
        this.GetObservable(DataContextProperty)
            .Select(v => v as PathEditorViewModel)
            .Select(v => v?.SelectedOperation.CombineLatest(v.IsClosed).ToUnit()
                ?? Observable.Return<Unit>(default))
            .Switch()
            .ObserveOn(AvaloniaScheduler.Instance)
            .Subscribe(_ => UpdateControlPointVisibility());
    }

    private void UpdateControlPointVisibility()
    {
        if (DataContext is PathEditorViewModel viewModel)
        {
            var controlPoints = canvas.Children.Where(i => i.Classes.Contains("control")).ToArray();
            foreach (var item in controlPoints)
            {
                item.IsVisible = false;
            }

            if (viewModel.SelectedOperation.Value is { } op
                && viewModel.PathGeometry.Value is { } geometry)
            {
                bool isClosed = geometry.IsClosed;
                int index = geometry.Segments.IndexOf(op);
                int nextIndex = (index + 1) % geometry.Segments.Count;

                if (isClosed || index != 0)
                {
                    foreach (var item in controlPoints.Where(v => v.DataContext == op))
                    {
                        if (Equals(item.Tag, "ControlPoint2") || Equals(item.Tag, "ControlPoint"))
                        {
                            item.IsVisible = true;
                        }
                    }
                }

                if (isClosed || nextIndex != 0)
                {
                    if (0 <= nextIndex && nextIndex < geometry.Segments.Count)
                    {
                        var next = geometry.Segments[nextIndex];
                        foreach (var item in controlPoints.Where(v => v.DataContext == next))
                        {
                            if (Equals(item.Tag, "ControlPoint1") || Equals(item.Tag, "ControlPoint"))
                                item.IsVisible = true;
                        }
                    }
                }
            }
        }
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

    private void OnOperationDetached(int index, PathSegment obj)
    {
        canvas.Children.RemoveAll(canvas.Children
            .Where(c => c is Thumb t && t.DataContext == obj)
            .Do(t => t.DataContext = null));
    }

    private static IObservable<Point> GetObservable(Thumb obj, CoreProperty<Graphics.Point> p)
    {
        return obj.GetObservable(DataContextProperty)
            .Select(v => (v as PathSegment)?.GetObservable(p) ?? Observable.Return((Graphics.Point)default))
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
    }

    private void OnOperationAttached(int index, PathSegment obj)
    {
        switch (obj)
        {
            case ArcSegment:
                {
                    Thumb t = CreateThumb();
                    t.DataContext = obj;
                    Bind(t, ArcSegment.PointProperty);

                    canvas.Children.Add(t);
                }
                break;

            case ConicSegment:
                {
                    Thumb c1 = CreateThumb();
                    c1.Tag = "ControlPoint";
                    c1.Classes.Add("control");
                    c1.DataContext = obj;
                    Bind(c1, ConicSegment.ControlPointProperty);

                    Thumb e = CreateThumb();
                    e.Tag = "EndPoint";
                    e.DataContext = obj;
                    Bind(e, ConicSegment.EndPointProperty);

                    canvas.Children.Add(e);
                    canvas.Children.Add(c1);
                }
                break;

            case CubicBezierSegment:
                {
                    Thumb c1 = CreateThumb();
                    c1.Classes.Add("control");
                    c1.Tag = "ControlPoint1";
                    c1.DataContext = obj;
                    Bind(c1, CubicBezierSegment.ControlPoint1Property);

                    Thumb c2 = CreateThumb();
                    c2.Classes.Add("control");
                    c2.Tag = "ControlPoint2";
                    c2.DataContext = obj;
                    Bind(c2, CubicBezierSegment.ControlPoint2Property);

                    Thumb e = CreateThumb();
                    e.Tag = "EndPoint";
                    e.DataContext = obj;
                    Bind(e, CubicBezierSegment.EndPointProperty);

                    canvas.Children.Add(e);
                    canvas.Children.Add(c2);
                    canvas.Children.Add(c1);
                }
                break;

            case LineSegment:
                {
                    Thumb t = CreateThumb();
                    t.DataContext = obj;
                    Bind(t, LineSegment.PointProperty);

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

            case QuadraticBezierSegment:
                {
                    Thumb c1 = CreateThumb();
                    c1.Tag = "ControlPoint";
                    c1.Classes.Add("control");
                    c1.DataContext = obj;
                    Bind(c1, QuadraticBezierSegment.ControlPointProperty);

                    Thumb e = CreateThumb();
                    e.Tag = "EndPoint";
                    e.DataContext = obj;
                    Bind(e, QuadraticBezierSegment.EndPointProperty);

                    canvas.Children.Add(e);
                    canvas.Children.Add(c1);
                }
                break;
        }

        UpdateControlPointVisibility();
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
        thumb.AddHandler(PointerReleasedEvent, OnThumbPointerReleased, handledEventsToo: true);
        var flyout = new FAMenuFlyout();
        var delete = new MenuFlyoutItem
        {
            Text = Strings.Delete,
            IconSource = new SymbolIconSource
            {
                Symbol = Symbol.Delete
            }
        };
        delete.Click += OnDeleteClicked;
        flyout.ItemsSource = new[] { delete };

        thumb.ContextFlyout = flyout;

        return thumb;
    }

    private void OnThumbPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Right
            && sender is Thumb { ContextFlyout: { } flyout } thumb)
        {
            flyout.ShowAt(thumb);
        }
    }

    private void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: PathSegment op }
            && DataContext is PathEditorViewModel { Context.Value.Group.Value: { } group })
        {
            int index = group.List.Value?.IndexOf(op) ?? -1;
            if (index >= 0)
                group.RemoveItem(index);
        }
    }

    private void OnThumbDragCompleted(object? sender, VectorEventArgs e)
    {

    }

    private void OnThumbDragStarted(object? sender, VectorEventArgs e)
    {
        if (sender is Thumb { DataContext: PathSegment op } t
            && DataContext is PathEditorViewModel { Context.Value.Group.Value: { } group } viewModel)
        {
            foreach (ListItemEditorViewModel<PathSegment> item in group.Items)
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

            if (!t.Classes.Contains("control"))
            {
                viewModel.SelectedOperation.Value = op;
            }
        }
    }

    private static CoreProperty<Graphics.Point>? GetProperty(Thumb t)
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

    private static CoreProperty<Graphics.Point>[] GetControlPointProperty(object datacontext)
    {
        return datacontext switch
        {
            ConicSegment => [ConicSegment.ControlPointProperty],
            CubicBezierSegment => [CubicBezierSegment.ControlPoint1Property, CubicBezierSegment.ControlPoint2Property],
            QuadraticBezierSegment => [QuadraticBezierSegment.ControlPointProperty],
            _ => [],
        };
    }

    // KeyModifier
    private void OnThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (sender is Thumb { DataContext: PathSegment op } t
            && DataContext is PathEditorViewModel { PathGeometry.Value: { } geometry } viewModel)
        {
            var delta = new Graphics.Vector((float)(e.Vector.X / Scale), (float)(e.Vector.Y / Scale));
            CoreProperty<Graphics.Point>? prop = GetProperty(t);
            if (prop != null)
            {
                Graphics.Point point = op.GetValue(prop);
                op.SetValue(prop, point + delta);
                if (!t.Classes.Contains("control"))
                {
                    CoreProperty<Graphics.Point>[] props = GetControlPointProperty(t.DataContext);
                    if (props.Length > 0)
                    {
                        var prop2 = props[^1];
                        op.SetValue(prop2, op.GetValue(prop2) + delta);
                    }

                    int index = geometry.Segments.IndexOf(op);
                    int nextIndex = (index + 1) % geometry.Segments.Count;

                    if (0 <= nextIndex && nextIndex < geometry.Segments.Count)
                    {
                        var nextOp = geometry.Segments[nextIndex];
                        props = GetControlPointProperty(nextOp);
                        if (props.Length > 0)
                        {
                            var prop2 = props[0];
                            nextOp.SetValue(prop2, nextOp.GetValue(prop2) + delta);
                        }
                    }
                }
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
        if (sender is MenuItem item
            && DataContext is PathEditorViewModel
            {
                PathGeometry.Value: { } geometry,
                Context.Value.Group.Value: { } group
            })
        {
            int index = geometry.Segments.Count;
            Graphics.Point lastPoint = default;
            if (index > 0)
            {
                PathSegment lastOp = geometry.Segments[index - 1];
                lastPoint = lastOp switch
                {
                    ArcSegment arc => arc.Point,
                    CubicBezierSegment cub => cub.EndPoint,
                    ConicSegment con => con.EndPoint,
                    LineSegment line => line.Point,
                    MoveOperation move => move.Point,
                    QuadraticBezierSegment quad => quad.EndPoint,
                    _ => default
                };
            }

            Graphics.Point point = (_clickPoint / Scale).ToBtlPoint();
            if (Matrix.TryInvert(out var mat))
            {
                point = mat.ToBtlMatrix().Transform(point);
            }

            PathSegment? obj = item.Tag switch
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

            if (obj != null)
            {
                group.AddItem(obj);
            }
        }
    }
}
