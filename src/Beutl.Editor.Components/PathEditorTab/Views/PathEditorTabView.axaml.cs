using System.Reactive;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using Avalonia.Threading;
using Beutl.Composition;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Media;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings.Extensions;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Editor.Components.PathEditorTab.Views;

public partial class PathEditorTabView : UserControl, IPathEditorView
{
    public static readonly DirectProperty<PathEditorTabView, double> ScaleProperty =
        AvaloniaProperty.RegisterDirect<PathEditorTabView, double>(nameof(Scale),
            o => o.Scale);

    public static readonly StyledProperty<Matrix> MatrixProperty =
        AvaloniaProperty.Register<PathEditorTabView, Matrix>(nameof(Matrix), Matrix.Identity);

    private double _scale = 1;
    private Point _clickPoint;

    private readonly PathEditorInteraction _interaction;
    private readonly Flyout _pointSettingsFlyout = new() { Placement = PlacementMode.Center };
    private readonly ScrollViewer _pointSettingsScroll = new();
    private bool _updatingCoordinates;
    private bool _compact;

    public PathEditorTool Tool { get => _interaction.Tool; set => _interaction.Tool = value; }

    private IDisposable? _disposable;

    private IDisposable? _strokeBindingRevoker;
    private IDisposable? _fillBindingRevoker;


    public PathEditorTabView()
    {
        InitializeComponent();
        PointProperties.Bind(DataContextProperty, this.GetObservable(DataContextProperty));
        _interaction = new PathEditorInteraction(this, canvas,
            delta => Matrix *= Matrix.CreateTranslation(delta), Zoom, Fit, ToolSlot);

        canvas.AddHandler(PointerPressedEvent, OnCanvasPointerPressed, RoutingStrategies.Tunnel);
        PointPropertyList.AddHandler(PropertyEditor.ValueChangedEvent, OnInspectorPropertyChanged, handledEventsToo: true);
        PointPropertyList.AddHandler(PropertyEditor.ValueConfirmedEvent, OnInspectorPropertyChanged, handledEventsToo: true);

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
            .Select(v => v as PathEditorTabViewModel)
            .Select(v => v?.SelectedOperation.CombineLatest(v.IsClosed).ToUnit()
                         ?? Observable.ReturnThenNever<Unit>(default))
            .Switch()
            .ObserveOnUIDispatcher()
            .Subscribe(_ => Refresh());

        // 個別にBindingするのではなく、一括で位置を変更する
        this.GetObservable(DataContextProperty)
            .Select(v => v as PathEditorTabViewModel)
            .Select(v => v?.EditorContext.GetService<IPreviewPlayer>()?.AfterRendered ?? Observable.ReturnThenNever(Unit.Default))
            .Switch()
            .CombineLatest(this.GetObservable(ScaleProperty), this.GetObservable(MatrixProperty))
            .Subscribe(_ => UpdateThumbPosition());

        this.GetObservable(DataContextProperty)
            .Select(v => v as PathEditorTabViewModel)
            .Select(v => v?.EditorContext.GetService<IPreviewPlayer>()?.AfterRendered ?? Observable.ReturnThenNever(Unit.Default))
            .Switch()
            .CombineLatest(view.GetObservable(PathGeometryControl.FigureProperty))
            .Subscribe(_ => UpdateBackgroundGeometry());

        this.GetObservable(MatrixProperty)
            .Subscribe(m =>
            {
                double scale = Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12);
                path.StrokeThickness = 1.5 / Math.Max(scale, 0.05);
                ZoomValueButton.Content = $"{scale * 100:0}%";
            });
        this.GetObservable(BoundsProperty).Subscribe(_ => UpdateResponsiveLayout());

        StrokeToggleButton.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(v => BindToggledBrush(v, ref _strokeBindingRevoker, Shape.StrokeProperty, "AccentFillColorDefaultBrush"));

        FillToggleButton.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(v => BindToggledBrush(v, ref _fillBindingRevoker, Shape.FillProperty, "ControlFillColorDefaultBrush"));
    }

    private void BindToggledBrush(bool? isChecked, ref IDisposable? revoker, StyledProperty<Avalonia.Media.IBrush?> property, string resourceKey)
    {
        revoker?.Dispose();
        revoker = null;
        if (isChecked == true)
        {
            revoker = path.Bind(
                property,
                new DynamicResourceExtension(resourceKey));
        }
        else
        {
            path.SetValue(property, null);
        }
    }

    public bool CanDragPoint(Thumb thumb) => _interaction.CanDragPoint(thumb);

    public void Refresh()
    {
        UpdateControlPointVisibility();
        view.SelectedOperations = GetSelectedAnchors().Select(t => t.DataContext).OfType<PathSegment>().ToArray();
        UpdateThumbPosition();
        UpdateBackgroundGeometry();
        UpdatePointProperties();
        view.InvalidateVisual();
    }

    private void UpdateResponsiveLayout()
    {
        bool compact = Bounds.Width < 560;
        CompactSettingsButton.IsVisible = compact;
        ZoomOutButton.IsVisible = ZoomInButton.IsVisible = Bounds.Width >= 320;
        Inspector.IsVisible = !compact;
        panel.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 240);
        _pointSettingsScroll.MaxHeight = Math.Max(80, Bounds.Height - 48);
        if (compact == _compact) return;
        _compact = compact;
        _pointSettingsFlyout.Hide();
        if (compact)
        {
            PropertiesScroll.Content = null;
            PointProperties.Width = 240;
            _pointSettingsScroll.Content = PointProperties;
            _pointSettingsFlyout.Content = _pointSettingsScroll;
        }
        else
        {
            _pointSettingsFlyout.Content = null;
            _pointSettingsScroll.Content = null;
            PointProperties.Width = double.NaN;
            PropertiesScroll.Content = PointProperties;
        }
    }

    private void ShowPointSettingsClicked(object? sender, RoutedEventArgs e)
    {
        _pointSettingsFlyout.ShowAt(this);
    }

    private void UpdatePointProperties()
    {
        if (DataContext is not PathEditorTabViewModel model) return;
        var clock = model.EditorContext.GetRequiredService<IEditorClock>();
        var composition = new CompositionContext(clock.CurrentTime.Value);
        BtlPoint[] points = GetSelectedAnchors().Select(PathEditorHelper.GetProperty)
            .Where(p => p != null).Select(p => p!.GetValue(composition)).ToArray();
        _updatingCoordinates = true;
        try
        {
            // Multiple anchors retain the group-translation field. A single anchor uses
            // the real point property editors, including their animation and reset menus.
            PointPosition.IsVisible = points.Length > 1;
            PointPosition.IsEnabled = points.Length > 1;
            PointPosition.FirstValue = points.Length == 0 ? 0 : (points.Min(p => p.X) + points.Max(p => p.X)) / 2;
            PointPosition.SecondValue = points.Length == 0 ? 0 : (points.Min(p => p.Y) + points.Max(p => p.Y)) / 2;
        }
        finally { _updatingCoordinates = false; }
    }

    private void OnInspectorPropertyChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        // Refresh after the standard editor and its engine-resource bindings have applied the edit.
        Dispatcher.UIThread.Post(Refresh);
    }

    private void OnPointPositionChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (!_updatingCoordinates && e is PropertyEditorValueChangedEventArgs<(float X, float Y)> args)
            _interaction.PreviewSelectionPosition(new(args.NewValue.X, args.NewValue.Y));
        e.Handled = true;
    }

    private void OnPointPositionConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (!_updatingCoordinates) _interaction.CommitSelectionPosition();
        e.Handled = true;
    }

    private Point ViewportCenter => new(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
    private void ZoomInClicked(object? sender, RoutedEventArgs e) => Zoom(1.2, ViewportCenter);
    private void ZoomOutClicked(object? sender, RoutedEventArgs e) => Zoom(1 / 1.2, ViewportCenter);
    private void FitClicked(object? sender, RoutedEventArgs e) => Fit(false);

    private void UpdateControlPointVisibility()
    {
        if (DataContext is PathEditorTabViewModel viewModel)
        {
            ControlPointVisibilityHelper.Update(
                canvas,
                viewModel.SelectedOperation.Value,
                viewModel.PathFigure.Value,
                new CompositionContext(viewModel.EditorContext.GetRequiredService<IEditorClock>().CurrentTime.Value));
        }
    }

    private void UpdateBackgroundGeometry()
    {
        Dispatcher.UIThread.Post(() =>
        {
            bool applied = false;
            if (DataContext is PathEditorTabViewModel { GeometryResource.Value: { } handle })
            {
                // Read holds the resource's owner off, so ApplyTo cannot walk a figure list the owner is
                // midway through replacing.
                applied = handle.Read(geometry =>
                {
                    using (var context = new GeometryContext { FillType = geometry.FillType })
                    {
                        geometry.ApplyTo(context);
                        string s = context.NativeObject.ToSvgPathData();

                        var newGeometry = Avalonia.Media.PathGeometry.Parse(s);
                        newGeometry.FillRule = geometry.FillType == PathFillType.Winding
                            ? Avalonia.Media.FillRule.NonZero
                            : Avalonia.Media.FillRule.EvenOdd;
                        path.Data = newGeometry;
                    }
                });
            }

            if (!applied)
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
            if (SkipUpdatePosition) return;
            if (DataContext is PathEditorTabViewModel viewModel)
            {
                var clock = viewModel.EditorContext.GetRequiredService<IEditorClock>();
                PathEditorHelper.UpdateThumbPositions(canvas, this, new CompositionContext(clock.CurrentTime.Value));
                _interaction.RefreshOverlays();
                view.InvalidateVisual();
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

    private void Fit(bool selectionOnly)
    {
        Thumb[] thumbs = selectionOnly ? GetSelectedAnchors() : canvas.Children.OfType<Thumb>().ToArray();
        if (thumbs.Length == 0 || !Matrix.TryInvert(out var inverse)) return;
        Point[] points = thumbs.Select(t => inverse.Transform(PathEditorHelper.GetCanvasPosition(t))).ToArray();
        double left = points.Min(p => p.X), top = points.Min(p => p.Y);
        double width = Math.Max(1, points.Max(p => p.X) - left);
        double height = Math.Max(1, points.Max(p => p.Y) - top);
        double availableWidth = canvas.Bounds.Width;
        double scale = Math.Clamp(Math.Min(Math.Max(1, availableWidth - 120) / width,
            Math.Max(1, canvas.Bounds.Height - 80) / height), 0.05, 64);
        Matrix = Matrix.CreateTranslation(-left - width / 2, -top - height / 2)
            * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(availableWidth / 2, canvas.Bounds.Height / 2);
    }

    private void Zoom(double ratio, Point center)
    {
        if (ratio == 0) { Matrix = Matrix.Identity; return; }
        double scale = Math.Sqrt(Matrix.M11 * Matrix.M11 + Matrix.M12 * Matrix.M12);
        if (!double.IsFinite(scale) || scale <= 0) return;
        ratio = Math.Clamp(scale * ratio, 0.05, 64) / scale;
        Matrix *= Matrix.CreateTranslation(-center.X, -center.Y)
            * Matrix.CreateScale(ratio, ratio) * Matrix.CreateTranslation(center.X, center.Y);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!new Rect(canvas.Bounds.Size).Contains(e.GetPosition(canvas))) return;
        if (SkipUpdatePosition) { e.Handled = true; return; }
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
            Zoom(Math.Pow(1.2, e.Delta.Y), e.GetPosition(canvas));
        else
        {
            Vector delta = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                ? new Vector(e.Delta.Y, e.Delta.X) : e.Delta;
            Matrix *= Matrix.CreateTranslation(delta * 24);
        }
        e.Handled = true;
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
        PathEditorHelper.DeleteSegment(sender, DataContext as PathEditorTabViewModel);
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
        if (sender is FARadioMenuFlyoutItem button1)
        {
            tag = button1.Tag as string;
        }
        else if (sender is RadioButton button2)
        {
            tag = button2.Tag as string;
        }

        if (tag != null && DataContext is PathEditorTabViewModel viewModel)
        {
            PathEditorHelper.ApplyDragMode(viewModel, tag);
        }
    }

    private void AddOpClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is FAMenuFlyoutItem item
            && DataContext is PathEditorTabViewModel viewModel)
        {
            PathEditorHelper.AddSegmentAt(viewModel, item.Tag, _clickPoint, Scale, Matrix);
        }
    }

    public Thumb? FindThumb(PathSegment segment, IProperty<BtlPoint> property)
    {
        return PathEditorHelper.FindThumb(canvas, segment, property);
    }

    public Thumb? FindAnchorThumb(PathSegment segment)
    {
        return canvas.Children.FirstOrDefault(v =>
            ReferenceEquals(v.DataContext, segment) && !v.Classes.Contains("control")) as Thumb;
    }

    public Thumb[] GetSelectedAnchors()
    {
        return PathEditorHelper.GetSelectedAnchors(canvas);
    }

    private void ResetZoomClicked(object? sender, RoutedEventArgs e)
    {
        Matrix = Matrix.Identity;
    }
}
