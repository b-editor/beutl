using System.Reactive;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Beutl.Composition;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Media;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings.Extensions;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Editor.Components.PathEditorTab.Views;

public partial class PathEditorView : UserControl, IPathEditorView
{
    public static readonly StyledProperty<int> SceneWidthProperty =
        AvaloniaProperty.Register<PathEditorView, int>(nameof(SceneWidth));

    public static readonly DirectProperty<PathEditorView, double> ScaleProperty =
        AvaloniaProperty.RegisterDirect<PathEditorView, double>(nameof(Scale),
            o => o.Scale);

    public static readonly StyledProperty<Matrix> MatrixProperty =
        AvaloniaProperty.Register<PathEditorView, Matrix>(nameof(Matrix), Matrix.Identity);

    private readonly PathEditorInteraction _interaction;
    public PathEditorTool Tool { get => _interaction.Tool; set => _interaction.Tool = value; }
    public Action<Vector>? PanViewport { get; set; }
    private double _scale = 1;
    private Point _clickPoint;
    private IDisposable? _disposable;

    public PathEditorView()
    {
        InitializeComponent();
        _interaction = new PathEditorInteraction(this, canvas, delta => PanViewport?.Invoke(delta));
        canvas.AddHandler(PointerPressedEvent, OnCanvasPointerPressed, RoutingStrategies.Tunnel);

        view.GetObservable(PathGeometryControl.FigureProperty)
            .Subscribe(geo => Dispatcher.UIThread.Post(() =>
            {
                // DataContext inheritance can still be walking the visual children here.
                // Rebuild after that traversal, and discard obsolete figure notifications.
                if (!ReferenceEquals(view.Figure, geo)) return;
                PathEditorHelper.ReplaceSegmentThumbs(canvas, geo, ref _disposable, OnOperationAttached, OnOperationDetached);
                Refresh();
            }));

        // 選択されているアンカーまたは、PathGeometry.IsClosedが変更されたとき、
        // アンカーの可視性を変更する
        this.GetObservable(DataContextProperty)
            .Select(v => v as PathEditorViewModel)
            .Select(v => v?.SelectedOperation.CombineLatest(v.IsClosed).ToUnit()
                ?? Observable.ReturnThenNever<Unit>(default))
            .Switch()
            .ObserveOnUIDispatcher()
            .Subscribe(_ => Refresh());

        // 個別にBindingするのではなく、一括で位置を変更する
        // TODO: Scale, Matrixが変わった時に位置がずれる
        this.GetObservable(DataContextProperty)
            .Select(v => v as PathEditorViewModel)
            .Select(v => v?.EditorContext.GetService<IPreviewPlayer>()?.AfterRendered ?? Observable.ReturnThenNever(Unit.Default))
            .Switch()
            .CombineLatest(this.GetObservable(ScaleProperty), this.GetObservable(MatrixProperty))
            .Subscribe(_ => UpdateThumbPosition());
    }

    public bool CanDragPoint(Thumb thumb) => _interaction.CanDragPoint(thumb);

    public void SetToolbarHost(Panel host) => _interaction.SetToolbarHost(host);

    public void Refresh()
    {
        UpdateControlPointVisibility();
        view.SelectedOperations = GetSelectedAnchors().Select(t => t.DataContext).OfType<PathSegment>().ToArray();
        UpdateThumbPosition();
        view.InvalidateVisual();
    }

    private void UpdateControlPointVisibility()
    {
        if (DataContext is PathEditorViewModel viewModel)
        {
            ControlPointVisibilityHelper.Update(
                canvas,
                viewModel.SelectedOperation.Value,
                viewModel.PathFigure.Value,
                new CompositionContext(viewModel.EditorContext.GetRequiredService<IEditorClock>().CurrentTime.Value));
        }
    }

    public void UpdateThumbPosition()
    {
        if (SkipUpdatePosition) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (SkipUpdatePosition) return;
            if (DataContext is PathEditorViewModel viewModel)
            {
                var clock = viewModel.EditorContext.GetRequiredService<IEditorClock>();
                PathEditorHelper.UpdateThumbPositions(canvas, this, new CompositionContext(clock.CurrentTime.Value));
                _interaction.RefreshOverlays();
                view.InvalidateVisual();
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
        PathEditorHelper.RemoveSegmentThumbs(canvas, obj);
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
        return PathEditorHelper.CreateEditorThumb(OnDeleteClicked);
    }

    private void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        PathEditorHelper.DeleteSegment(sender, DataContext as PathEditorViewModel);
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
        if (sender is FARadioMenuFlyoutItem button && DataContext is PathEditorViewModel viewModel)
        {
            PathEditorHelper.ApplyDragMode(viewModel, button.Tag);
        }
    }

    private void AddOpClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is FAMenuFlyoutItem item
            && DataContext is PathEditorViewModel viewModel)
        {
            PathEditorHelper.AddSegmentAt(viewModel, item.Tag, _clickPoint, Scale, Matrix);
        }
    }

    public Thumb? FindThumb(PathSegment segment, IProperty<BtlPoint> property)
    {
        return PathEditorHelper.FindThumb(canvas, segment, property);
    }

    public Thumb[] GetSelectedAnchors()
    {
        return PathEditorHelper.GetSelectedAnchors(canvas);
    }
}
