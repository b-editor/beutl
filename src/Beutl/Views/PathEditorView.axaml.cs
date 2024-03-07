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

using FluentAvalonia.UI.Controls;

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
                _disposable = geo?.Operations.ForEachItem(
                    OnOperationAttached,
                    OnOperationDetached,
                    () => canvas.Children.RemoveAll(canvas.Children
                        .Where(c => c is Thumb)
                        .Do(t => t.DataContext = null)));
            });

        this.GetObservable(DataContextProperty)
            .Select(v => (v as PathEditorViewModel)?.SelectedOperation
                ?? Observable.Return<PathOperation?>(default))
            .Switch()
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
                int index = geometry.Operations.IndexOf(op);
                int nextIndex = (index + 1) % geometry.Operations.Count;

                foreach (var item in controlPoints.Where(v => v.DataContext == op))
                {
                    if (Equals(item.Tag, "ControlPoint2") || Equals(item.Tag, "ControlPoint"))
                    {
                        item.IsVisible = true;
                    }
                }

                if (0 <= nextIndex && nextIndex < geometry.Operations.Count)
                {
                    var next = geometry.Operations[nextIndex];
                    foreach (var item in controlPoints.Where(v => v.DataContext == next))
                    {
                        if (Equals(item.Tag, "ControlPoint1") || Equals(item.Tag, "ControlPoint"))
                            item.IsVisible = true;
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

            case ConicOperation:
                {
                    Thumb c1 = CreateThumb();
                    c1.Tag = "ControlPoint";
                    c1.Classes.Add("control");
                    c1.DataContext = obj;
                    Bind(c1, ConicOperation.ControlPointProperty);

                    Thumb e = CreateThumb();
                    e.Tag = "EndPoint";
                    e.DataContext = obj;
                    Bind(e, ConicOperation.EndPointProperty);

                    canvas.Children.Add(e);
                    canvas.Children.Add(c1);
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
        if (sender is MenuFlyoutItem { DataContext: PathOperation op }
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
            case ArcOperation:
                return ArcOperation.PointProperty;

            case ConicOperation:
                switch (t.Tag)
                {
                    case "ControlPoint":
                        return ConicOperation.ControlPointProperty;
                    case "EndPoint":
                        return ConicOperation.EndPointProperty;
                }
                break;

            case CubicBezierOperation:
                switch (t.Tag)
                {
                    case "ControlPoint1":
                        return CubicBezierOperation.ControlPoint1Property;

                    case "ControlPoint2":
                        return CubicBezierOperation.ControlPoint2Property;
                    case "EndPoint":
                        return CubicBezierOperation.EndPointProperty;
                }
                break;

            case LineOperation:
                return LineOperation.PointProperty;

            case MoveOperation:
                return MoveOperation.PointProperty;

            case QuadraticBezierOperation:
                switch (t.Tag)
                {
                    case "ControlPoint":
                        return QuadraticBezierOperation.ControlPointProperty;
                    case "EndPoint":
                        return QuadraticBezierOperation.EndPointProperty;
                }
                break;
        }

        return null;
    }

    private static CoreProperty<Graphics.Point>[] GetControlPointProperty(object datacontext)
    {
        return datacontext switch
        {
            ConicOperation => [ConicOperation.ControlPointProperty],
            CubicBezierOperation => [CubicBezierOperation.ControlPoint1Property, CubicBezierOperation.ControlPoint2Property],
            QuadraticBezierOperation => [QuadraticBezierOperation.ControlPointProperty],
            _ => [],
        };
    }

    // KeyModifier
    private void OnThumbDragDelta(object? sender, VectorEventArgs e)
    {
        if (sender is Thumb { DataContext: PathOperation op } t
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

                    int index = geometry.Operations.IndexOf(op);
                    int nextIndex = (index + 1) % geometry.Operations.Count;

                    if (0 <= nextIndex && nextIndex < geometry.Operations.Count)
                    {
                        var nextOp = geometry.Operations[nextIndex];
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
            int index = geometry.Operations.Count;
            Graphics.Point lastPoint = default;
            if (index > 0)
            {
                PathOperation lastOp = geometry.Operations[index - 1];
                lastPoint = lastOp switch
                {
                    ArcOperation arc => arc.Point,
                    CubicBezierOperation cub => cub.EndPoint,
                    ConicOperation con => con.EndPoint,
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

            PathOperation? obj = item.Tag switch
            {
                "Arc" => new ArcOperation() { Point = point },
                "Close" => new CloseOperation(),
                "Conic" => new ConicOperation()
                {
                    EndPoint = point,
                    ControlPoint = new(float.Lerp(point.X, lastPoint.X, 0.5f), float.Lerp(point.Y, lastPoint.Y, 0.5f))
                },
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
                group.AddItem(obj);
            }
        }
    }
}
