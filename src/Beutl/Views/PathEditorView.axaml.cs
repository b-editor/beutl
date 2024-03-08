#pragma warning disable CS0618

using System.Collections.Immutable;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.ReactiveUI;
using Avalonia.Styling;
using Avalonia.Xaml.Interactivity;

using Beutl.Animation;
using Beutl.Media;
using Beutl.Rendering;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;

using FluentAvalonia.UI.Controls;

using Reactive.Bindings.Extensions;

using ReactiveUI;

using BtlPoint = Beutl.Graphics.Point;
using BtlVector = Beutl.Graphics.Vector;

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
            .ObserveOnUIDispatcher()
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

    private static IObservable<Point> GetObservable(Thumb obj, CoreProperty<BtlPoint> p)
    {
        return obj.GetObservable(DataContextProperty)
            .Select(v => (v as PathSegment)?.GetObservable(p) ?? Observable.Return((BtlPoint)default))
            .Switch()
            .Select(v => v.ToAvaPoint())
            .ObserveOnUIDispatcher();
    }

    private static void Bind(Thumb t, CoreProperty<BtlPoint> p)
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
        var thumb = new Thumb()
        {
            Theme = this.FindResource("ControlPointThumb") as ControlTheme
        };
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

        Interaction.GetBehaviors(thumb).Add(new ThumbDragBehavior());

        return thumb;
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

    private static CoreProperty<BtlPoint>? GetProperty(Thumb t)
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

    private static CoreProperty<BtlPoint>[] GetControlPointProperty(object datacontext)
    {
        return datacontext switch
        {
            ConicSegment => [ConicSegment.ControlPointProperty],
            CubicBezierSegment => [CubicBezierSegment.ControlPoint1Property, CubicBezierSegment.ControlPoint2Property],
            QuadraticBezierSegment => [QuadraticBezierSegment.ControlPointProperty],
            _ => [],
        };
    }

    private static CoreProperty<BtlPoint>? GetControlPointProperty(object datacontext, int i)
    {
        return datacontext switch
        {
            ConicSegment => ConicSegment.ControlPointProperty,
            CubicBezierSegment => i == 0 ? CubicBezierSegment.ControlPoint1Property : CubicBezierSegment.ControlPoint2Property,
            QuadraticBezierSegment => QuadraticBezierSegment.ControlPointProperty,
            _ => null,
        };
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint pt = e.GetCurrentPoint(canvas);
        if (pt.Properties.IsRightButtonPressed)
        {
            _clickPoint = pt.Position;
        }
    }

    private void ToggleDragModeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem button && DataContext is PathEditorViewModel viewModel)
        {
            viewModel.Symmetry.Value = false;
            viewModel.Asymmetry.Value = false;
            viewModel.Separately.Value = false;

            switch (button.Tag)
            {
                case "Symmetry":
                    viewModel.Symmetry.Value = true;
                    break;
                case "Asymmetry":
                    viewModel.Asymmetry.Value = true;
                    break;
                case "Separately":
                    viewModel.Separately.Value = true;
                    break;
            }
        }
    }

    private void AddOpClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item
            && DataContext is PathEditorViewModel
            {
                PathGeometry.Value: { } geometry,
                Context.Value.Group.Value: { } group
            })
        {
            int index = geometry.Segments.Count;
            BtlPoint lastPoint = default;
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

            BtlPoint point = (_clickPoint / Scale).ToBtlPoint();
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

    private sealed class ThumbDragBehavior : Behavior<Thumb>
    {
        private ThumbDragState? _dragState;
        private ThumbDragState[]? _coordDragStates;
        private Point? _lastPoint;
        private CancellationTokenSource? _cts;

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject is { })
            {
                AssociatedObject.AddHandler(PointerPressedEvent, OnThumbPointerPressed, handledEventsToo: true);
                AssociatedObject.AddHandler(PointerReleasedEvent, OnThumbPointerReleased, handledEventsToo: true);
                AssociatedObject.AddHandler(PointerMovedEvent, OnThumbPointerMoved, handledEventsToo: true);
                AssociatedObject.AddHandler(PointerCaptureLostEvent, OnThumbPointerCaptureLost, handledEventsToo: true);
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject is { })
            {
                AssociatedObject.RemoveHandler(PointerPressedEvent, OnThumbPointerPressed);
                AssociatedObject.RemoveHandler(PointerReleasedEvent, OnThumbPointerReleased);
                AssociatedObject.RemoveHandler(PointerMovedEvent, OnThumbPointerMoved);
                AssociatedObject.RemoveHandler(PointerCaptureLostEvent, OnThumbPointerCaptureLost);
            }
        }

        private void OnThumbPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (_lastPoint.HasValue)
            {
                e.Handled = true;

                var vector = _lastPoint.Value;
            }

            _lastPoint = null;
        }

        private void OnThumbPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == MouseButton.Right
                && AssociatedObject is { ContextFlyout: { } flyout })
            {
                flyout.ShowAt(AssociatedObject);
            }

            if (e.InitialPressMouseButton == MouseButton.Left && _lastPoint.HasValue)
            {
                e.Handled = true;

                PathEditorView? parent = AssociatedObject?.FindLogicalAncestorOfType<PathEditorView>();
                if (parent is { DataContext: PathEditorViewModel { Element.Value: { } element } viewModel })
                {
                    IRecordableCommand? command = _dragState?.CreateCommand([]);
                    if (_coordDragStates?.Length > 0)
                    {
                        command = _coordDragStates.Aggregate(command, (a, b) => a.Append(b.CreateCommand([])));
                    }

                    if (command != null)
                    {
                        command = command.WithStoables([element]);

                        command.DoAndRecord(viewModel.EditViewModel.CommandRecorder);
                    }
                }

            }

            _coordDragStates = null;
            _dragState = null;
            _lastPoint = null;
        }

        // 戻り値: PathSegmentがCubicBezierSegmentの場合、intはどちらの制御点かを表す
        private static (PathSegment, int)[] GetAnchors(PathEditorViewModel viewModel, PathGeometry geometry, PathSegment segment, object? tag)
        {
            if (tag is not string s || geometry.Segments.Count <= 1) return [];

            int index = geometry.Segments.IndexOf(segment);
            int previndex = (index - 1 + geometry.Segments.Count) % geometry.Segments.Count;
            if (s == "ControlPoint1")
            {
                PathSegment prev = geometry.Segments[previndex];
                return [(prev, 1)];
            }
            else if (s == "ControlPoint2")
            {
                return [(segment, 0)];
            }
            else if (s == "ControlPoint")
            {
                PathSegment? selected = viewModel.SelectedOperation.Value;
                if (selected != segment)
                {
                    PathSegment prev = geometry.Segments[previndex];
                    return [(prev, 1)];
                }
                else
                {
                    return [(segment, 0)];
                }
            }
            else
            {
                return [];
            }
        }

        private void OnThumbPointerMoved(object? sender, PointerEventArgs e)
        {
            PathEditorView? parent = AssociatedObject?.FindLogicalAncestorOfType<PathEditorView>();
            if (AssociatedObject is not { DataContext: PathSegment segment }
                || parent is not { DataContext: PathEditorViewModel { PathGeometry.Value: { } geometry } viewModel }
                || !_lastPoint.HasValue)
            {
                return;
            }

            Point vector = e.GetPosition(AssociatedObject) - _lastPoint.Value;

            var delta = new BtlVector((float)(vector.X / parent.Scale), (float)(vector.Y / parent.Scale));
            if (_dragState != null)
            {
                _dragState.Move(delta);
                if (_dragState.Thumb is { } thumb)
                {
                    Canvas.SetLeft(thumb, Canvas.GetLeft(thumb) + delta.X);
                    Canvas.SetTop(thumb, Canvas.GetTop(thumb) + delta.Y);
                }

                if (_coordDragStates != null)
                {
                    if (AssociatedObject.Classes.Contains("control"))
                    {
                        if (viewModel.Symmetry.Value || viewModel.Asymmetry.Value)
                        {
                            // ControlPointからAnchor(複数)を取得
                            // つながっているAnchorの反対側ごとに、角度、長さを計算
                            BtlPoint point = _dragState.GetSampleValue();
                            foreach ((PathSegment, int) item in GetAnchors(viewModel, geometry, segment, AssociatedObject.Tag))
                            {
                                if (item.Item1.TryGetEndPoint(out BtlPoint endpoint))
                                {
                                    BtlPoint d = endpoint - point;
                                    float angle = MathF.Atan2(d.X, d.Y);
                                    angle -= MathF.PI / 2;

                                    foreach (ThumbDragState c in _coordDragStates)
                                    {
                                        float length;
                                        if (viewModel.Symmetry.Value)
                                        {
                                            length = MathF.Sqrt((d.X * d.X) + (d.Y * d.Y));
                                        }
                                        else
                                        {
                                            BtlPoint d2 = endpoint - c.GetSampleValue();
                                            length = MathF.Sqrt((d2.X * d2.X) + (d2.Y * d2.Y));
                                        }

                                        float x = MathF.Cos(angle) * length;
                                        float y = MathF.Sin(angle) * length;
                                        c.SetValue(endpoint + new BtlPoint(x, -y));
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (ThumbDragState item in _coordDragStates)
                        {
                            item.Move(delta);
                            if (item.Thumb is { } thumb1)
                            {
                                Canvas.SetLeft(thumb1, Canvas.GetLeft(thumb1) + delta.X);
                                Canvas.SetTop(thumb1, Canvas.GetTop(thumb1) + delta.Y);
                            }
                        }
                    }
                }
            }
        }

        private void SetSelectedOperation(PathEditorViewModel viewModel, PathSegment segment)
        {
            if (AssociatedObject != null
                && viewModel is { Context.Value.Group.Value: { } group })
            {
                if (!AssociatedObject.Classes.Contains("control"))
                {
                    foreach (ListItemEditorViewModel<PathSegment> item in group.Items)
                    {
                        if (item.Context is PathOperationEditorViewModel itemvm)
                        {
                            if (ReferenceEquals(itemvm.Value.Value, segment))
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
                }

                if (!AssociatedObject.Classes.Contains("control"))
                {
                    viewModel.SelectedOperation.Value = segment;
                }
            }
        }

        private void OnThumbPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            PathEditorView? parent = AssociatedObject?.FindLogicalAncestorOfType<PathEditorView>();
            if (AssociatedObject is not { DataContext: PathSegment segment }
                || parent is not { DataContext: PathEditorViewModel { PathGeometry.Value: { } geometry } viewModel })
            {
                return;
            }

            e.Handled = true;
            _lastPoint = e.GetPosition(AssociatedObject);

            SetSelectedOperation(viewModel, segment);

            CoreProperty<BtlPoint>? prop = GetProperty(AssociatedObject);
            if (prop != null)
            {
                _dragState = CreateThumbDragState(viewModel, segment, prop);

                if (!AssociatedObject.Classes.Contains("control"))
                {
                    var list = new List<ThumbDragState>();
                    CoordinateControlPoint(list, parent, viewModel, geometry, segment);
                    _coordDragStates = [.. list];
                }
                else
                {
                    var list = new List<ThumbDragState>();
                    CoordinateAnotherControlPoint(list, parent, viewModel, geometry, segment, prop);
                    _coordDragStates = [.. list];
                }
            }
        }

        private void CoordinateControlPoint(
            List<ThumbDragState> list,
            PathEditorView view,
            PathEditorViewModel viewModel,
            PathGeometry geometry,
            PathSegment segment)
        {
            CoreProperty<BtlPoint>[] props = GetControlPointProperty(segment);
            if (props.Length > 0)
            {
                var state = CreateThumbDragState(viewModel, segment, props[^1]);
                state.Thumb = GetThumb(view, state.Target, state.Property);
                list.Add(state);
            }

            int index = geometry.Segments.IndexOf(segment);
            int nextIndex = (index + 1) % geometry.Segments.Count;

            if (0 <= nextIndex && nextIndex < geometry.Segments.Count)
            {
                PathSegment nextSegment = geometry.Segments[nextIndex];
                props = GetControlPointProperty(nextSegment);
                if (props.Length > 0)
                {
                    var state = CreateThumbDragState(viewModel, nextSegment, props[0]);
                    state.Thumb = GetThumb(view, state.Target, state.Property);
                    list.Add(state);
                }
            }
        }

        private static Thumb? GetThumb(PathEditorView view, PathSegment segment, CoreProperty<BtlPoint> property)
        {
            return view.canvas.Children.FirstOrDefault(v => ReferenceEquals(v.DataContext, segment) && Equals(v.Tag, property.Name)) as Thumb;
        }

        private void CoordinateAnotherControlPoint(
            List<ThumbDragState> list,
            PathEditorView view,
            PathEditorViewModel viewModel,
            PathGeometry geometry,
            PathSegment segment,
            // [ControlPoint, ControlPoint1, ControlPoint2] のいずれか
            CoreProperty<BtlPoint> property)
        {
            int index = geometry.Segments.IndexOf(segment);
            if (index < 0 || geometry.Segments.Count == 0) return;

            if (segment is CubicBezierSegment)
            {
                PathSegment? asegment = null;
                PathSegment? anchor = null;
                int apropIndex = -1;

                if (property == CubicBezierSegment.ControlPoint1Property)
                {
                    int aindex = (index - 1 + geometry.Segments.Count) % geometry.Segments.Count;
                    asegment = geometry.Segments[aindex];
                    apropIndex = 1;
                    anchor = asegment;
                }
                else if (property == CubicBezierSegment.ControlPoint2Property)
                {
                    int aindex = (index + 1) % geometry.Segments.Count;
                    asegment = geometry.Segments[aindex];
                    apropIndex = 0;
                    anchor = segment;
                }

                if (asegment != null)
                {
                    CoreProperty<BtlPoint>? aproperty = GetControlPointProperty(asegment, apropIndex);
                    if (aproperty != null)
                    {
                        var state = CreateThumbDragState(viewModel, asegment, aproperty);
                        state.Anchor = anchor;
                        state.Thumb = GetThumb(view, state.Target, state.Property);
                        list.Add(state);
                    }
                }
            }
            else if (segment is QuadraticBezierSegment or ConicSegment)
            {
                void Add(int aindex, int apropIndex, PathSegment? anchor)
                {
                    PathSegment asegment = geometry.Segments[aindex];
                    anchor ??= asegment;

                    CoreProperty<BtlPoint>? aproperty = GetControlPointProperty(asegment, apropIndex);
                    if (aproperty != null)
                    {
                        ThumbDragState state = CreateThumbDragState(viewModel, asegment, aproperty);
                        state.Anchor = anchor;
                        state.Thumb = GetThumb(view, state.Target, state.Property);
                        list.Add(state);
                    }
                }

                PathSegment? selected = viewModel.SelectedOperation.Value;
                if (selected != segment)
                {
                    Add((index - 1 + geometry.Segments.Count) % geometry.Segments.Count, 1, segment);
                }
                else
                {
                    Add((index + 1) % geometry.Segments.Count, 0, null);
                }
            }
        }

        private ThumbDragState CreateThumbDragState(
            PathEditorViewModel viewModel,
            PathSegment segment,
            CoreProperty<BtlPoint> property)
        {
            var editViewModel = viewModel.EditViewModel;
            var element = viewModel.Element.Value!;
            int rate = editViewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
            TimeSpan globalkeyTime = editViewModel.CurrentTime.Value;
            TimeSpan localKeyTime = element != null ? globalkeyTime - element.Start : globalkeyTime;

            if (segment.Animations.FirstOrDefault(v => v.Property == property) is KeyFrameAnimation<BtlPoint> animation)
            {
                TimeSpan keyTime = animation.UseGlobalClock ? globalkeyTime : localKeyTime;
                keyTime = keyTime.RoundToRate(rate);

                (IKeyFrame? prev, IKeyFrame? next) = animation.KeyFrames.GetPreviousAndNextKeyFrame(keyTime);

                if (next?.KeyTime == keyTime)
                    return new(property, segment, next as KeyFrame<BtlPoint>, null);

                return new(property, segment, prev as KeyFrame<BtlPoint>, next as KeyFrame<BtlPoint>);
            }

            return new(property, segment, null, null);
        }
    }

    private sealed class ThumbDragState
    {
        public ThumbDragState(
            CoreProperty<BtlPoint> property,
            PathSegment target,
            KeyFrame<BtlPoint>? previous,
            KeyFrame<BtlPoint>? next,
            // このThumbがControlPointの時、点線でつながっているポイントを指定する
            PathSegment? anchor = null)
        {
            Previous = previous;
            Next = next;
            Anchor = anchor;
            Property = property;
            Target = target;
            OldPreviousValue = previous?.Value ?? default;
            OldNextValue = next?.Value ?? default;
            OldValue = target.GetValue(property);
        }

        public float? Length { get; }

        public KeyFrame<BtlPoint>? Previous { get; }

        public KeyFrame<BtlPoint>? Next { get; }

        public Thumb? Thumb { get; set; }

        public CoreProperty<BtlPoint> Property { get; }

        public PathSegment Target { get; }

        public PathSegment? Anchor { get; set; }

        public BtlPoint OldPreviousValue { get; }

        public BtlPoint OldNextValue { get; }

        public BtlPoint OldValue { get; }

        public BtlPoint GetSampleValue()
        {
            if (Previous != null)
            {
                return Previous.GetValue(KeyFrame<BtlPoint>.ValueProperty);
            }
            else
            {
                return Target.GetValue(Property);
            }
        }

        public void SetValue(BtlPoint point)
        {
            if (Previous == null && Next == null)
            {
                Target.SetValue(Property, point);
            }
            else
            {
                CoreProperty<BtlPoint> prop = KeyFrame<BtlPoint>.ValueProperty;

                Previous?.SetValue(prop, point);
            }
        }

        public void Move(BtlVector delta)
        {
            if (Previous == null && Next == null)
            {
                Target.SetValue(Property, Target.GetValue(Property) + delta);
            }
            else
            {
                CoreProperty<BtlPoint> prop = KeyFrame<BtlPoint>.ValueProperty;
                Previous?.SetValue(prop, Previous.GetValue(prop) + delta);

                Next?.SetValue(prop, Next.GetValue(prop) + delta);
            }
        }

        public IRecordableCommand? CreateCommand(ImmutableArray<IStorable?> storables)
        {
            if (Previous == null && Next == null)
            {
                return RecordableCommands.Edit(Target, Property, Target.GetValue(Property), OldValue)
                    .WithStoables(storables);
            }
            else
            {
                return RecordableCommands.Append(
                    Previous != null && Previous.Value != OldPreviousValue
                        ? RecordableCommands.Edit(Previous, KeyFrame<BtlPoint>.ValueProperty, Previous.Value, OldPreviousValue).WithStoables(storables)
                        : null,
                    Next != null && Next.Value != OldNextValue
                        ? RecordableCommands.Edit(Next, KeyFrame<BtlPoint>.ValueProperty, Next.Value, OldNextValue).WithStoables(storables)
                        : null);
            }
        }
    }
}
