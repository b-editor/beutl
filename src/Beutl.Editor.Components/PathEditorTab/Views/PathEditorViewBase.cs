using System.Reactive;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Beutl.Composition;
using Beutl.Editor.Components.PathEditorTab.Services;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Media;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings.Extensions;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Editor.Components.PathEditorTab.Views;

// The state and thumb handling shared by the path editor tab and the preview overlay.
public abstract class PathEditorViewBase : UserControl, IPathEditorView
{
    public static readonly DirectProperty<PathEditorViewBase, double> ScaleProperty =
        AvaloniaProperty.RegisterDirect<PathEditorViewBase, double>(nameof(Scale),
            o => o.Scale);

    public static readonly StyledProperty<Matrix> MatrixProperty =
        AvaloniaProperty.Register<PathEditorViewBase, Matrix>(nameof(Matrix), Matrix.Identity);

    private Canvas _canvas = null!;
    private PathGeometryControl _view = null!;
    private PathEditorInteraction _interaction = null!;
    private Point _clickPoint;
    private IDisposable? _disposable;

    // Test seam: in the app, the toolbar and the keys switch tools in the interaction.
    internal PathEditorTool Tool { get => _interaction.Tool; set => _interaction.Tool = value; }

    private protected PathEditorInteraction Interaction => _interaction;

    public bool SkipUpdatePosition { get; set; }

    public Matrix Matrix
    {
        get => GetValue(MatrixProperty);
        set => SetValue(MatrixProperty, value);
    }

    public abstract double Scale { get; }

    // Call after InitializeComponent. The interaction already refreshes the view while it is created.
    private protected void InitializeEditor(
        Canvas canvas,
        PathGeometryControl view,
        Action<Vector>? pan = null,
        Action<double, Point>? zoom = null,
        Action<bool>? fit = null,
        Panel? toolbarHost = null)
    {
        _canvas = canvas;
        _view = view;
        _interaction = new PathEditorInteraction(this, canvas, pan, zoom, fit, toolbarHost);
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

        // 選択や IsClosed が変わったら表示を更新する。
        this.GetObservable(DataContextProperty)
            .Select(v => v as IPathEditorContext)
            .Select(v => v?.SelectedOperation.CombineLatest(v.IsClosed).ToUnit()
                         ?? Observable.ReturnThenNever<Unit>(default))
            .Switch()
            .ObserveOnUIDispatcher()
            .Subscribe(_ => Refresh());

        // 個別にBindingするのではなく、一括で位置を変更する
        this.GetObservable(DataContextProperty)
            .Select(v => v as IPathEditorContext)
            .Select(v => v?.EditorContext.GetService<IPreviewPlayer>()?.AfterRendered ?? Observable.ReturnThenNever(Unit.Default))
            .Switch()
            .CombineLatest(this.GetObservable(ScaleProperty), this.GetObservable(MatrixProperty))
            .Subscribe(_ => UpdateThumbPosition());
    }

    public bool CanDragPoint(Thumb thumb) => _interaction.CanDragPoint(thumb);

    public abstract void Refresh();

    private protected void UpdateControlPointVisibility()
    {
        if (DataContext is IPathEditorContext viewModel)
        {
            ControlPointVisibilityHelper.Update(
                _canvas,
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
            if (DataContext is IPathEditorContext viewModel)
            {
                var clock = viewModel.EditorContext.GetRequiredService<IEditorClock>();
                PathEditorHelper.UpdateThumbPositions(_canvas, this, new CompositionContext(clock.CurrentTime.Value));
                _interaction.RefreshOverlays();
                _view.InvalidateVisual();
            }
        }, DispatcherPriority.MaxValue);
    }

    private void OnOperationDetached(int index, PathSegment obj)
    {
        PathEditorHelper.RemoveSegmentThumbs(_canvas, obj);
    }

    private void OnOperationAttached(int index, PathSegment obj)
    {
        Thumb[] thumbs = PathEditorHelper.CreateThumbs(obj, CreateThumb);
        _canvas.Children.AddRange(thumbs);

        UpdateControlPointVisibility();
        UpdateThumbPosition();
    }

    private Thumb CreateThumb()
    {
        return PathEditorHelper.CreateEditorThumb(OnDeleteClicked);
    }

    private void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        PathEditorHelper.DeleteSegment(sender, DataContext as IPathEditorContext);
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint pt = e.GetCurrentPoint(_canvas);
        if (pt.Properties.IsRightButtonPressed)
        {
            _clickPoint = pt.Position;
        }
    }

    protected void AddOpClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is FAMenuFlyoutItem item
            && DataContext is IPathEditorContext viewModel)
        {
            PathEditorHelper.AddSegmentAt(viewModel, item.Tag, _clickPoint, Scale, Matrix);
        }
    }

    public Thumb? FindThumb(PathSegment segment, IProperty<BtlPoint> property)
    {
        return PathEditorHelper.FindThumb(_canvas, segment, property);
    }

    public Thumb[] GetSelectedAnchors()
    {
        return PathEditorHelper.GetSelectedAnchors(_canvas);
    }
}
