using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

using Beutl.Media;
using Beutl.ViewModels;

using FluentAvalonia.UI.Controls;

using Reactive.Bindings.Extensions;

using BtlPoint = Beutl.Graphics.Point;
using BtlVector = Beutl.Graphics.Vector;

namespace Beutl.Views.Tools;

public partial class PathEditorTab : UserControl, IPathEditorView
{
    public static readonly DirectProperty<PathEditorTab, double> ScaleProperty =
        AvaloniaProperty.RegisterDirect<PathEditorTab, double>(nameof(Scale),
            o => o.Scale);

    public static readonly StyledProperty<Matrix> MatrixProperty =
        AvaloniaProperty.Register<PathEditorTab, Matrix>(nameof(Matrix), Matrix.Identity);

    private double _scale = 1;
    private Point _clickPoint;

    private bool _pressed;
    private Point _position;
    private Point _startPosition;

    private bool _rangeSelection;
    private Border? _rangeSelectionBorder;

    private IDisposable? _disposable;

    private IDisposable? _strokeBindingRevoker;
    private IDisposable? _fillBindingRevoker;

    private List<PathPointDragState>? _dragStates;

    public PathEditorTab()
    {
        InitializeComponent();
        canvas.AddHandler(PointerPressedEvent, OnCanvasPointerPressed, RoutingStrategies.Tunnel);

        view.GetObservable(PathGeometryControl.FigureProperty)
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
            .Select(v => v as PathEditorTabViewModel)
            .Select(v => v?.SelectedOperation.CombineLatest(v.IsClosed).ToUnit()
                ?? Observable.Return<Unit>(default))
            .Switch()
            .ObserveOnUIDispatcher()
            .Subscribe(_ => UpdateControlPointVisibility());

        // 個別にBindingするのではなく、一括で位置を変更する
        this.GetObservable(DataContextProperty)
            .Select(v => v as PathEditorTabViewModel)
            .Select(v => v?.EditViewModel.Player.AfterRendered ?? Observable.Return(Unit.Default))
            .Switch()
            .CombineLatest(this.GetObservable(ScaleProperty), this.GetObservable(MatrixProperty))
            .Subscribe(_ => UpdateThumbPosition());

        this.GetObservable(DataContextProperty)
            .Select(v => v as PathEditorTabViewModel)
            .Select(v => v?.EditViewModel.Player.AfterRendered ?? Observable.Return(Unit.Default))
            .Switch()
            .CombineLatest(view.GetObservable(PathGeometryControl.FigureProperty))
            .Subscribe(_ => UpdateBackgroundGeometry());

        this.GetObservable(MatrixProperty)
            .Subscribe(m => path.StrokeThickness = 2 / m.M11);

        StrokeToggleButton.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(v =>
            {
                _strokeBindingRevoker?.Dispose();
                _strokeBindingRevoker = null;
                if (v == true)
                {
                    _strokeBindingRevoker = path.Bind(
                        Shape.StrokeProperty,
                        new DynamicResourceExtension("AccentFillColorDefaultBrush"));
                }
                else
                {
                    path.Stroke = null;
                }
            });

        FillToggleButton.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(v =>
            {
                _fillBindingRevoker?.Dispose();
                _fillBindingRevoker = null;
                if (v == true)
                {
                    _fillBindingRevoker = path.Bind(
                        Shape.FillProperty,
                        new DynamicResourceExtension("ControlFillColorDefaultBrush"));
                }
                else
                {
                    path.Fill = null;
                }
            });
    }

    private void UpdateControlPointVisibility()
    {
        if (DataContext is PathEditorTabViewModel viewModel)
        {
            Control[] controlPoints = canvas.Children.Where(i => i.Classes.Contains("control")).ToArray();
            foreach (Control item in controlPoints)
            {
                item.IsVisible = false;
            }

            if (viewModel.SelectedOperation.Value is { } op
                && viewModel.PathFigure.Value is { } figure)
            {
                bool isClosed = figure.IsClosed;
                int index = figure.Segments.IndexOf(op);
                int nextIndex = (index + 1) % figure.Segments.Count;

                if (isClosed || index != 0)
                {
                    foreach (Control? item in controlPoints.Where(v => v.DataContext == op))
                    {
                        if (Equals(item.Tag, "ControlPoint2") || Equals(item.Tag, "ControlPoint"))
                        {
                            item.IsVisible = true;
                        }
                    }
                }

                if (isClosed || nextIndex != 0)
                {
                    if (0 <= nextIndex && nextIndex < figure.Segments.Count)
                    {
                        PathSegment next = figure.Segments[nextIndex];
                        foreach (Control? item in controlPoints.Where(v => v.DataContext == next))
                        {
                            if (Equals(item.Tag, "ControlPoint1") || Equals(item.Tag, "ControlPoint"))
                                item.IsVisible = true;
                        }
                    }
                }
            }
        }
    }

    private void UpdateBackgroundGeometry()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is PathEditorTabViewModel { PathFigure.Value: { } figure, PathGeometry.Value: { } geometry } viewModel)
            {
                using (var context = new GeometryContext() { FillType = geometry.FillType })
                {
                    geometry.ApplyTo(context);
                    string s = context.NativeObject.ToSvgPathData();

                    var newGeometry = Avalonia.Media.PathGeometry.Parse(s);
                    newGeometry.FillRule = geometry.FillType == PathFillType.Winding ? Avalonia.Media.FillRule.NonZero : Avalonia.Media.FillRule.EvenOdd;
                    path.Data = newGeometry;
                }
            }
            else
            {
                path.Data = null;
            }
        });
    }

    public void UpdateThumbPosition()
    {
        if (SkipUpdatePosition) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is PathEditorTabViewModel viewModel)
            {
                foreach (Thumb thumb in canvas.Children.OfType<Thumb>())
                {
                    if (thumb.DataContext is PathSegment segment)
                    {
                        CoreProperty<BtlPoint>? prop = PathEditorHelper.GetProperty(thumb);
                        if (prop != null)
                        {
                            Point point = segment.GetValue(prop).ToAvaPoint();
                            point = point.Transform(Matrix);
                            point *= Scale;

                            Canvas.SetLeft(thumb, point.X);
                            Canvas.SetTop(thumb, point.Y);
                        }
                    }
                }
            }
        }, DispatcherPriority.MaxValue);
    }

    public bool SkipUpdatePosition { get; set; }

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

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        const float ZoomSpeed = 1.2f;

        Point pos = Matrix.Invert().Transform(e.GetPosition(canvas));
        double x = pos.X;
        double y = pos.Y;
        double delta = e.Delta.Y;
        double realDelta = Math.Sign(delta) * Math.Abs(delta);

        double ratio = Math.Pow(ZoomSpeed, realDelta);

        var a = new Matrix(ratio, 0, 0, ratio, x - (ratio * x), y - (ratio * y));

        Matrix = a * Matrix;

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pressed)
        {
            Point position = e.GetPosition(this);
            if (_rangeSelection && _rangeSelectionBorder != null)
            {
                Rect rect = new Rect(_startPosition, position).Normalize();
                _rangeSelectionBorder.Margin = new(rect.X, rect.Y, 0, 0);
                _rangeSelectionBorder.Width = rect.Width;
                _rangeSelectionBorder.Height = rect.Height;

                foreach (Thumb? item in canvas.Children.OfType<Thumb>()
                    .Where(c => !c.Classes.Contains("control")))
                {
                    Point p = PathEditorHelper.GetCanvasPosition(item);
                    PathPointDragBehavior.SetIsSelected(item, rect.Contains(p));
                }
            }
            else
            {
                Point delta = position - _position;
                Matrix *= Matrix.CreateTranslation((float)delta.X, (float)delta.Y);
            }

            _position = position;

            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        OnReleased();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        OnReleased();
    }

    private void OnReleased()
    {
        _pressed = false;
        if (_rangeSelectionBorder != null)
        {
            panel.Children.Remove(_rangeSelectionBorder);
            _rangeSelectionBorder = null;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPoint pointerPoint = e.GetCurrentPoint(this);
        _pressed = pointerPoint.Properties.IsLeftButtonPressed;
        _startPosition = _position = pointerPoint.Position;
        if (_pressed)
        {
            _rangeSelection = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (_rangeSelection)
            {
                _rangeSelectionBorder = new()
                {
                    BorderBrush = TimelineSharedObject.SelectionPen.Brush,
                    BorderThickness = new(0.5),
                    Background = TimelineSharedObject.SelectionFillBrush,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
                };
                panel.Children.Add(_rangeSelectionBorder);
            }

            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is Key.Left or Key.Up or Key.Right or Key.Down
            && DataContext is PathEditorTabViewModel { Element.Value: { } element } viewModel
            && _dragStates?.Count > 0)
        {
            _dragStates.Select(v => v.CreateCommand([]))
                .Aggregate((IRecordableCommand?)null, (a, b) => a.Append(b))!
                .WithStoables([element])
                .DoAndRecord(viewModel.EditViewModel.CommandRecorder);
        }

        _dragStates = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            GetSelectedAnchors()
                .ForEach(i => PathPointDragBehavior.SetIsSelected(i, false));
            e.Handled = true;
        }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.A)
        {
            canvas.Children.OfType<Thumb>()
                .Where(c => !c.Classes.Contains("control"))
                .ForEach(i => PathPointDragBehavior.SetIsSelected(i, true));

            e.Handled = true;
        }
        if (e.Key is Key.Left or Key.Up or Key.Right or Key.Down)
        {
            if (_dragStates == null
                && DataContext is PathEditorTabViewModel { SelectedOperation.Value: { } segment, PathFigure.Value: { } figure } viewModel
                && FindAnchorThumb(segment) is Thumb thumb)
            {
                _dragStates = CreateDragState(thumb, figure, segment);
            }

            if (_dragStates != null)
            {
                BtlVector vector = e.Key switch
                {
                    Key.Left => new(-1, 0),
                    Key.Up => new(0, -1),
                    Key.Right => new(1, 0),
                    Key.Down => new(0, 1),
                    _ => default
                };

                _dragStates.ForEach(d => d.Move(vector));
                e.Handled = true;
            }
        }
    }

    private List<PathPointDragState> CreateDragState(Thumb thumb, PathFigure figure, PathSegment segment)
    {
        CoreProperty<BtlPoint>? prop = PathEditorHelper.GetProperty(thumb);
        var list = new List<PathPointDragState>();
        if (prop != null && DataContext is PathEditorTabViewModel viewModel)
        {
            PathPointDragState dragState = PathPointDragBehavior.CreateThumbDragState(viewModel, segment, prop);
            dragState.Thumb = thumb;
            list.Add(dragState);

            if (!thumb.Classes.Contains("control"))
            {
                PathPointDragBehavior.CoordinateControlPoint(list, this, viewModel, figure, segment);
                foreach (Thumb anchor in GetSelectedAnchors())
                {
                    if (anchor == thumb) continue;

                    CoreProperty<BtlPoint>? prop2 = PathEditorHelper.GetProperty(anchor);
                    if (anchor.DataContext is PathSegment s && prop2 != null)
                    {
                        PathPointDragState d = PathPointDragBehavior.CreateThumbDragState(viewModel, s, prop2);
                        d.Thumb = anchor;
                        list.Add(d);

                        PathPointDragBehavior.CoordinateControlPoint(list, this, viewModel, figure, s);
                    }
                }
            }
            else
            {
                PathPointDragBehavior.CoordinateAnotherControlPoint(list, this, viewModel, figure, segment, prop);
            }
        }

        return list;
    }

    private void OnOperationDetached(int index, PathSegment obj)
    {
        canvas.Children.RemoveAll(canvas.Children
            .Where(c => c is Thumb t && t.DataContext == obj)
            .Do(t => t.DataContext = null));
    }

    private void OnOperationAttached(int index, PathSegment obj)
    {
        Thumb[] thumbs = PathEditorHelper.CreateThumbs(obj, CreateThumb);
        canvas.Children.AddRange(thumbs);

        UpdateControlPointVisibility();
        UpdateThumbPosition();
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

        Interaction.GetBehaviors(thumb).Add(new PathPointDragBehavior());

        return thumb;
    }

    private void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: PathSegment op }
            && DataContext is PathEditorTabViewModel { FigureContext.Value.Group.Value: { } group })
        {
            int index = group.List.Value?.IndexOf(op) ?? -1;
            if (index >= 0)
                group.RemoveItem(index);
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

    private void ToggleDragModeClick(object? sender, RoutedEventArgs e)
    {
        string? tag = null;
        if (sender is RadioMenuFlyoutItem button1)
        {
            tag = button1.Tag as string;
        }
        else if (sender is RadioButton button2)
        {
            tag = button2.Tag as string;
        }

        if (tag != null && DataContext is PathEditorTabViewModel viewModel)
        {
            viewModel.Symmetry.Value = false;
            viewModel.Asymmetry.Value = false;
            viewModel.Separately.Value = false;

            switch (tag)
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
            && DataContext is PathEditorTabViewModel
            {
                PathFigure.Value: { } figure,
                FigureContext.Value.Group.Value: { } group
            })
        {
            int index = figure.Segments.Count;
            BtlPoint lastPoint = default;
            if (index > 0)
            {
                PathSegment lastOp = figure.Segments[index - 1];
                lastOp.TryGetEndPoint(out lastPoint);
            }

            BtlPoint point = (_clickPoint / Scale).ToBtlPoint();
            if (Matrix.TryInvert(out Matrix mat))
            {
                point = mat.ToBtlMatrix().Transform(point);
            }

            PathSegment? obj = PathEditorHelper.CreateSegment(item.Tag, point, lastPoint);

            if (obj != null)
            {
                group.AddItem(obj);
            }
        }
    }

    public Thumb? FindThumb(PathSegment segment, CoreProperty<BtlPoint> property)
    {
        return canvas.Children.FirstOrDefault(v => ReferenceEquals(v.DataContext, segment) && Equals(v.Tag, property.Name)) as Thumb;
    }

    public Thumb? FindAnchorThumb(PathSegment segment)
    {
        return canvas.Children.FirstOrDefault(v => ReferenceEquals(v.DataContext, segment) && !v.Classes.Contains("control")) as Thumb;
    }

    public Thumb[] GetSelectedAnchors()
    {
        return canvas.Children.OfType<Thumb>()
            .Where(c => !c.Classes.Contains("control") && PathPointDragBehavior.GetIsSelected(c))
            .ToArray();
    }

    private void ResetZoomClicked(object? sender, RoutedEventArgs e)
    {
        Matrix = Matrix.Identity;
    }
}
