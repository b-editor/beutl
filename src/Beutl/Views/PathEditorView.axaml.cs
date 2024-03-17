using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

using Beutl.Media;
using Beutl.ViewModels;

using FluentAvalonia.UI.Controls;

using Reactive.Bindings.Extensions;

using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Views;

public partial class PathEditorView : UserControl, IPathEditorView
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
            .Select(v => v as PathEditorViewModel)
            .Select(v => v?.SelectedOperation.CombineLatest(v.IsClosed).ToUnit()
                ?? Observable.Return<Unit>(default))
            .Switch()
            .ObserveOnUIDispatcher()
            .Subscribe(_ => UpdateControlPointVisibility());

        // 個別にBindingするのではなく、一括で位置を変更する
        // TODO: Scale, Matrixが変わった時に位置がずれる
        this.GetObservable(DataContextProperty)
            .Select(v => v as PathEditorViewModel)
            .Select(v => v?.PlayerViewModel?.AfterRendered ?? Observable.Return(Unit.Default))
            .Switch()
            .CombineLatest(this.GetObservable(ScaleProperty), this.GetObservable(MatrixProperty))
            .Subscribe(_ => UpdateThumbPosition());
    }

    private void UpdateControlPointVisibility()
    {
        if (DataContext is PathEditorViewModel viewModel)
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

    public void UpdateThumbPosition()
    {
        if (SkipUpdatePosition) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is PathEditorViewModel viewModel)
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
            && DataContext is PathEditorViewModel { FigureContext.Value.Group.Value: { } group })
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

    public Thumb[] GetSelectedAnchors()
    {
        return canvas.Children.OfType<Thumb>()
            .Where(c => !c.Classes.Contains("control") && PathPointDragBehavior.GetIsSelected(c))
            .ToArray();
    }
}
