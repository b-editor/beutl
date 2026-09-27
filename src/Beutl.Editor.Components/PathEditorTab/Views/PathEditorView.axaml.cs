using System.Reactive;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using Beutl.Composition;
using Beutl.Controls;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Media;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings.Extensions;
using BtlPoint = Beutl.Graphics.Point;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

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
            [!ThemeProperty] = new DynamicResourceExtension("PathEditorControlPointThumbTheme")
        };
        var flyout = new FAMenuFlyout();
        var delete = new FAMenuFlyoutItem
        {
            Text = Strings.Delete,
            IconSource = new FluentIconSource
            {
                Icon = Icon.Delete
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
        if (sender is FAMenuFlyoutItem { DataContext: PathSegment op }
            && DataContext is PathEditorViewModel viewModel
            && viewModel.FigureContext.Value is IPathFigureEditorContext figureContext)
        {
            int index = figureContext.GetSegmentIndex(op);
            if (index >= 0)
                figureContext.RemoveSegment(index);
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
        if (sender is FARadioMenuFlyoutItem button && DataContext is PathEditorViewModel viewModel)
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
        if (sender is FAMenuFlyoutItem item
            && DataContext is PathEditorViewModel viewModel
            && viewModel.PathFigure.Value is { } figure
            && viewModel.FigureContext.Value is IPathFigureEditorContext figureContext)
        {
            var clock = viewModel.EditorContext.GetRequiredService<IEditorClock>();
            int index = figure.Segments.Count;
            BtlPoint lastPoint = default;
            if (index > 0)
            {
                PathSegment lastOp = figure.Segments[index - 1];
                var ctx = new CompositionContext(clock.CurrentTime.Value);
                lastPoint = lastOp.GetEndPoint().GetValue(ctx);
            }

            BtlPoint point = (_clickPoint / Scale).ToBtlPoint();
            if (Matrix.TryInvert(out Matrix mat))
            {
                point = mat.ToBtlMatrix().Transform(point);
            }

            PathSegment? obj = PathEditorHelper.CreateSegment(item.Tag, point, lastPoint);

            if (obj != null)
            {
                figureContext.AddSegment(obj);
            }
        }
    }

    public Thumb? FindThumb(PathSegment segment, IProperty<BtlPoint> property)
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
