using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings.Extensions;
using Setter = Avalonia.Styling.Setter;

namespace Beutl.Editor.Components.TimelineTab.Views;

/*
 * 移動アニメーション中にUndoを行うと、
 * 表示される位置がUndo前になる。
 * 解決するには、オブジェクトがUndo/Redoしたかを追跡するAPIを追加して、
 * Undo/Redoがされた場合アニメーションをキャンセルする必要がある。
 */

public sealed partial class ElementView : UserControl
{
    private readonly CompositeDisposable _disposables = [];
    private TimelineTabView? _timeline;
    private TimeSpan _pointerPosition;
    private static ColorPickerFlyout? s_colorPickerFlyout;
    private _ResizeBehavior? _resizeBehavior;

    public ElementView()
    {
        InitializeComponent();

        var cm = AppHelper.GetContextCommandManager?.Invoke();
        cm?.Attach(this, TimelineTabExtension.Instance);
        (border.ContextFlyout as FAMenuFlyout)!.Opening += OnContextFlyoutOpening;
        textBox.LostFocus += OnTextBoxLostFocus;
        this.SubscribeDataContextChange<ElementViewModel>(OnDataContextAttached, OnDataContextDetached);
    }

    private ElementViewModel ViewModel => (ElementViewModel)DataContext!;

    internal static double CalculateRightResizeX(
        double pointerX,
        double? afterStartX,
        double leftX,
        double? originalDurationWidth,
        bool ripple)
    {
        double x = ripple || afterStartX is null ? pointerX : Math.Min(afterStartX.Value, pointerX);

        if (originalDurationWidth is { } maxWidth)
        {
            x = Math.Min(x, leftX + maxWidth);
        }

        return x;
    }

    internal static double CalculateLeftResizeX(
        double pointerX,
        double? beforeEndX,
        double? rippleFloorX,
        bool ripple)
    {
        if (beforeEndX is null) return pointerX;

        double floor = ripple && rippleFloorX is { } f ? f : beforeEndX.Value;
        return Math.Max(floor, pointerX);
    }

    // Smallest Start among same-layer elements that end at or before the target's start —
    // the set ripple pushes left. Its Start hitting 0 bounds how far the left edge can grow.
    private static TimeSpan? GetLeftmostUpstreamStart(Scene scene, Element element)
    {
        TimeSpan? leftmost = null;
        foreach (Element other in scene.Children)
        {
            if (other != element && other.ZIndex == element.ZIndex && other.Range.End <= element.Start)
            {
                if (leftmost is not { } cur || other.Start < cur)
                {
                    leftmost = other.Start;
                }
            }
        }

        return leftmost;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.LeftCtrl)
        {
            _resizeBehavior?.OnLeftCtrlPressed(e);
        }
    }

    private void OnContextFlyoutOpening(object? sender, EventArgs e)
    {
        if (DataContext is not ElementViewModel viewModel) return;

        bool editable = viewModel.IsEditable.Value;
        change2OriginalDuration.IsEnabled = editable && viewModel.HasOriginalDuration();
        splitByCurrent.IsEnabled = editable && viewModel.Model.Range.Contains(viewModel.Timeline.EditorContext.GetRequiredService<IEditorClock>().CurrentTime.Value);
        // Not gated by `editable`: Group/Ungroup act on the selection's editable
        // members, so they stay valid from a locked clip's menu when other
        // selected clips are editable. CanGroup/CanUngroup already filter.
        groupSelectedElements.IsEnabled = viewModel.CanGroupSelectedElements();
        ungroupSelectedElements.IsEnabled = viewModel.CanUngroupSelectedElements();
        split.IsEnabled = editable;
        cut.IsEnabled = editable;
        delete.IsEnabled = editable;
        exclude.IsEnabled = editable;
        finishEditingAnimation.IsEnabled = editable;
        rename.IsEnabled = editable;
        enableElement.IsEnabled = editable;
        changeColor.IsEnabled = editable;
        // With the layer locked, clearing the element flag has no visible effect
        // (IsEditable stays false), so the toggle would read as broken.
        lockElement.IsEnabled = viewModel.LayerHeader.Value?.IsLocked.Value != true;
    }

    private void LockElement_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ElementViewModel viewModel) return;
        Element model = viewModel.Model;
        viewModel.Timeline.EditorContext.GetRequiredService<IElementAttributeService>()
            .SetLocked(model, !model.IsLocked);
    }

    private void OnDataContextDetached(ElementViewModel obj)
    {
        obj.AnimationRequested = (_, _) => Task.CompletedTask;
        obj.RenameRequested = () => { };
        obj.GetClickedTime = null;

        obj.GetMissingThumbnailIndices = null;
        thumbnailStrip.VisibleRangeChanged -= obj.OnVisibleRangeChanged;

        obj.ThumbnailReady -= OnThumbnailReady;
        obj.ThumbnailsClear -= OnThumbnailsClear;
        obj.WaveformChunkReady -= OnWaveformChunkReady;
        obj.WaveformClear -= OnWaveformClear;

        _disposables.Clear();
    }

    private void OnDataContextAttached(ElementViewModel obj)
    {
        obj.AnimationRequested = async (args, token) =>
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var animation1 = new Avalonia.Animation.Animation { Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0), Duration = TimeSpan.FromSeconds(0.25), FillMode = FillMode.Forward, Children = { new KeyFrame() { Cue = new Cue(0), Setters = { new Setter(MarginProperty, border.Margin), new Setter(WidthProperty, border.Width), } }, new KeyFrame() { Cue = new Cue(1), Setters = { new Setter(MarginProperty, args.BorderMargin), new Setter(WidthProperty, args.Width) } } } };
                var animation2 = new Avalonia.Animation.Animation { Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0), Duration = TimeSpan.FromSeconds(0.25), FillMode = FillMode.Forward, Children = { new KeyFrame() { Cue = new Cue(0), Setters = { new Setter(MarginProperty, obj.Margin.Value) } }, new KeyFrame() { Cue = new Cue(1), Setters = { new Setter(MarginProperty, args.Margin) } } } };

                Task task1 = animation1.RunAsync(border, token);
                Task task2 = animation2.RunAsync(this, token);
                await Task.WhenAll(task1, task2);
            });
        };
        obj.RenameRequested = () => Rename_Click(null, null!);
        obj.GetClickedTime = () => _pointerPosition;

        obj.GetMissingThumbnailIndices = thumbnailStrip.GetMissingIndices;
        thumbnailStrip.VisibleRangeChanged += obj.OnVisibleRangeChanged;

        obj.IsSelected
            .ObserveOnUIDispatcher()
            .Subscribe(v => ZIndex = v ? 5 : 0)
            .DisposeWith(_disposables);

        obj.ThumbnailReady += OnThumbnailReady;
        obj.ThumbnailsClear += OnThumbnailsClear;
        obj.WaveformChunkReady += OnWaveformChunkReady;
        obj.WaveformClear += OnWaveformClear;
    }

    private void OnThumbnailReady(int index, WriteableBitmap? thumbnail)
    {
        thumbnailStrip.SetThumbnail(index, thumbnail);
    }

    private void OnThumbnailsClear()
    {
        thumbnailStrip.ClearThumbnails();
    }

    private void OnWaveformChunkReady(WaveformChunk chunk)
    {
        waveformControl.SetChunk(chunk);
    }

    private void OnWaveformClear()
    {
        waveformControl.ClearChunks();
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _timeline = this.FindLogicalAncestorOfType<TimelineTabView>();

        BehaviorCollection behaviors = Interaction.GetBehaviors(this);
        behaviors.Clear();
        behaviors.Add(new _SelectBehavior());
        behaviors.Add(_resizeBehavior = new _ResizeBehavior());
        behaviors.Add(new _MoveBehavior());
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        _timeline = null;
        BehaviorCollection behaviors = Interaction.GetBehaviors(this);
        behaviors.Clear();
        _resizeBehavior = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point point = e.GetPosition(this);
        float scale = ViewModel.Timeline.Options.Value.Scale;
        _pointerPosition = point.X.PixelToTimeSpan(scale);
    }

    private void EnableElementClick(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsEditable.Value) return;

        Element model = ViewModel.Model;
        ViewModel.Timeline.EditorContext.GetRequiredService<IElementAttributeService>()
            .SetEnabled(model, !model.IsEnabled);
    }

    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        textBlock.IsVisible = true;
        textBox.IsVisible = false;
    }

    private void Rename_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ElementViewModel { IsEditable.Value: true }) return;

        textBlock.IsVisible = false;
        textBox.IsVisible = true;
        textBox.SelectAll();
        textBox.Focus();
    }

    private void SaveAsTemplate_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ElementViewModel viewModel) return;

        string defaultName = !string.IsNullOrWhiteSpace(viewModel.Model.Name)
            ? viewModel.Model.Name
            : TypeDisplayHelpers.GetLocalizedName(viewModel.Model.GetType());
        string uniqueName = ObjectTemplateService.Instance.GetUniqueName(defaultName);

        var flyout = new SaveAsTemplateFlyout { Text = uniqueName };
        flyout.Confirmed += (_, name) =>
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                ObjectTemplateService.Instance.AddFromInstance(viewModel.Model, name);
            }
        };
        flyout.ShowAt(this, true);
    }

    private void ChangeColor_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ElementViewModel { IsEditable.Value: true } viewModel) return;

        // ContextMenuから開いているので、閉じるのを待つ
        s_colorPickerFlyout ??= new ColorPickerFlyout();
        s_colorPickerFlyout.ColorPicker.Color = viewModel.Color.Value;
        s_colorPickerFlyout.ColorPicker.IsAlphaEnabled = false;
        s_colorPickerFlyout.ColorPicker.UseColorPalette = true;
        s_colorPickerFlyout.ColorPicker.IsCompact = true;
        s_colorPickerFlyout.ColorPicker.IsMoreButtonVisible = true;
        s_colorPickerFlyout.Placement = PlacementMode.Top;

        if (this.TryFindResource("PaletteColors", out object? colors)
            && colors is IEnumerable<Color> tcolors)
        {
            s_colorPickerFlyout.ColorPicker.CustomPaletteColors = tcolors;
        }

        s_colorPickerFlyout.Confirmed += OnColorPickerFlyoutConfirmed;
        s_colorPickerFlyout.Closed += OnColorPickerFlyoutClosed;

        s_colorPickerFlyout.ShowAt(border, true);
    }

    private void OnColorPickerFlyoutClosed(object? sender, EventArgs e)
    {
        s_colorPickerFlyout!.Confirmed -= OnColorPickerFlyoutConfirmed;
        s_colorPickerFlyout.Closed -= OnColorPickerFlyoutClosed;
    }

    private void OnColorPickerFlyoutConfirmed(ColorPickerFlyout sender, EventArgs args)
    {
        // Re-check editability at confirm time: the clip or its layer may have been locked while the
        // picker was open, and the open-time guard alone would let the confirm persist onto locked content.
        if (DataContext is ElementViewModel { IsEditable.Value: true } viewModel)
        {
            viewModel.Color.Value = sender.ColorPicker.Color;
        }
    }

    private TimeSpan RoundStartTime(TimeSpan time, float scale, bool flag, int? sameZIndex = null)
    {
        Element model = ViewModel.Model;
        TimelineTabViewModel timeline = ViewModel.Timeline;

        if (flag || !timeline.IsSnapEnabled.Value)
        {
            timeline.SnapBarPosition.Value = null;
            return time;
        }

        IEditorClock clock = timeline.EditorContext.GetRequiredService<IEditorClock>();
        IEnumerable<TimeSpan> candidates = SnapHelper
            .CollectElementCandidates(timeline.Scene.Children, model, sameZIndex)
            .Concat(SnapHelper.CollectSceneCandidates(timeline.Scene, clock.CurrentTime.Value));

        SnapResult result = SnapHelper.Snap(time, candidates, scale);
        timeline.SnapBarPosition.Value = result.DidSnap ? result.Time.TimeToPixel(scale) : null;
        return result.Time;
    }

    private sealed class _ResizeBehavior : Behavior<ElementView>
    {
        private record struct ElementResizeContext(
            ElementViewModel ViewModel,
            Element? Before,
            Element? After,
            TimeSpan RecordedStartTime,
            TimeSpan RecordedEndTime,
            TimeSpan? LeftmostUpstreamStart,
            TimeSpan? OriginalDuration);

        private enum TrimDragKind { None, Roll, Slide }

        private record struct TrimDragContext(
            TrimDragKind Kind,
            ElementViewModel Front,
            ElementViewModel? Middle,
            ElementViewModel Back,
            double InitialFrontWidth,
            double InitialMiddleLeft,
            double InitialMiddleWidth,
            double InitialBackLeft,
            double InitialBackWidth,
            double InitialPointerX);

        private bool _pressed;
        private AlignmentX _resizeType;
        private ElementResizeContext[] _resizeContexts = [];
        private TrimDragContext _trimDrag;

        public void OnLeftCtrlPressed(KeyEventArgs e)
        {
            if (AssociatedObject is { } view && !_pressed)
            {
                view.Cursor = null;
                _resizeType = AlignmentX.Center;
                e.Handled = true;
            }
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject == null) return;

            AssociatedObject.AddHandler(PointerMovedEvent, OnPointerMoved);
            AssociatedObject.border.AddHandler(PointerPressedEvent, OnBorderPointerPressed);
            AssociatedObject.border.AddHandler(PointerReleasedEvent, OnBorderPointerReleased);
            AssociatedObject.border.AddHandler(PointerMovedEvent, OnBorderPointerMoved);
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject != null)
            {
                AssociatedObject.RemoveHandler(PointerMovedEvent, OnPointerMoved);
                AssociatedObject.border.RemoveHandler(PointerPressedEvent, OnBorderPointerPressed);
                AssociatedObject.border.RemoveHandler(PointerReleasedEvent, OnBorderPointerReleased);
                AssociatedObject.border.RemoveHandler(PointerMovedEvent, OnBorderPointerMoved);
            }
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject is { ViewModel: { } viewModel } view)
            {
                Point point = e.GetPosition(view);
                float scale = viewModel.Timeline.Options.Value.Scale;
                TimeSpan pointerFrame = point.X.PixelToTimeSpan(scale);

                if (view._timeline is { } timeline && _pressed)
                {
                    pointerFrame = view.RoundStartTime(pointerFrame, scale, e.KeyModifiers.HasFlag(KeyModifiers.Alt));
                    point = point.WithX(pointerFrame.TimeToPixel(scale));
                    int rate = viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
                    double minWidth = TimeSpan.FromSeconds(1d / rate).TimeToPixel(scale);

                    if (_trimDrag.Kind is not TrimDragKind.None)
                    {
                        ApplyTrimDragPreview(point.X, minWidth);
                        e.Handled = true;
                        return;
                    }

                    if (view.Cursor != Cursors.Arrow && view.Cursor is { })
                    {
                        foreach (ElementResizeContext ctx in _resizeContexts)
                        {
                            double left = ctx.ViewModel.BorderMargin.Value.Left;

                            if (_resizeType == AlignmentX.Right)
                            {
                                // 右
                                double x = CalculateRightResizeX(
                                    point.X,
                                    ctx.After?.Start.TimeToPixel(scale),
                                    left,
                                    ctx.OriginalDuration?.TimeToPixel(scale),
                                    viewModel.Timeline.IsRippleEnabled.Value);

                                ctx.ViewModel.Width.Value = Math.Max(x - left, minWidth);
                            }
                            else if (_resizeType == AlignmentX.Left && pointerFrame >= TimeSpan.Zero)
                            {
                                // 左
                                double? rippleFloorX = ctx.LeftmostUpstreamStart is { } upstreamStart
                                    ? (ctx.RecordedStartTime - upstreamStart).TimeToPixel(scale)
                                    : null;
                                double x = CalculateLeftResizeX(
                                    point.X,
                                    ctx.Before?.Range.End.TimeToPixel(scale),
                                    rippleFloorX,
                                    viewModel.Timeline.IsRippleEnabled.Value);

                                double endPos = ctx.RecordedEndTime.TimeToPixel(scale);

                                double newWidth = endPos - x;
                                if (minWidth < newWidth)
                                {
                                    ctx.ViewModel.Width.Value = newWidth;
                                    ctx.ViewModel.BorderMargin.Value = new Thickness(x, 0, 0, 0);
                                }
                                else
                                {
                                    ctx.ViewModel.Width.Value = minWidth;
                                    ctx.ViewModel.BorderMargin.Value = new Thickness(endPos - minWidth, 0, 0, 0);
                                }
                            }
                        }

                        e.Handled = true;
                    }
                }
            }
        }

        private void ApplyTrimDragPreview(double pointerX, double minWidth)
        {
            TrimDragContext ctx = _trimDrag;
            double delta = pointerX - ctx.InitialPointerX;

            double floor = minWidth - ctx.InitialFrontWidth;
            double ceiling = ctx.InitialBackWidth - minWidth;
            if (floor > ceiling)
                return;

            double actualDelta = Math.Clamp(delta, floor, ceiling);

            ctx.Front.Width.Value = ctx.InitialFrontWidth + actualDelta;
            ctx.Back.BorderMargin.Value = new Thickness(ctx.InitialBackLeft + actualDelta, 0, 0, 0);
            ctx.Back.Width.Value = ctx.InitialBackWidth - actualDelta;

            if (ctx.Middle is { } middle)
            {
                middle.BorderMargin.Value = new Thickness(ctx.InitialMiddleLeft + actualDelta, 0, 0, 0);
                middle.Width.Value = ctx.InitialMiddleWidth;
            }
        }

        private void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is { _timeline: not null, ViewModel: { } viewModel } view)
            {
                if (viewModel.Timeline.IsRazorMode.Value)
                {
                    return;
                }

                if (!viewModel.IsEditable.Value)
                {
                    return;
                }

                PointerPoint point = e.GetCurrentPoint(view.border);
                bool leftButton = point.Properties.IsLeftButtonPressed
                                  && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Alt;

                // Slide is a body-drag gesture (SlideTool_Description: "Drag a clip"), so it must
                // start on the clip body where the cursor is Arrow, not only on the resize edge.
                if (leftButton && viewModel.Timeline.IsSlideMode.Value)
                {
                    if (TryStartSlideDrag(viewModel, e.GetPosition(view), view))
                    {
                        _pressed = true;
                    }

                    e.Handled = true;
                    return;
                }

                if (leftButton && view.Cursor != Cursors.Arrow && view.Cursor is not null)
                {
                    Point timelinePosition = e.GetPosition(view);
                    if (viewModel.Timeline.IsRollMode.Value)
                    {
                        if (TryStartRollDrag(viewModel, timelinePosition, view))
                        {
                            _pressed = true;
                        }

                        e.Handled = true;
                        return;
                    }

                    IReadOnlyList<ElementViewModel> relatedElements = viewModel.GetGroupOrSelectedElements()
                        .Where(el => el.IsEditable.Value)
                        .ToArray();

                    // リサイズタイプに応じて、同じ時間の要素のみをフィルタリング
                    IEnumerable<ElementViewModel> filteredElements;
                    if (_resizeType == AlignmentX.Right)
                    {
                        // 右端リサイズ: 同じEnd時間の要素のみ
                        TimeSpan targetEndTime = viewModel.Model.Range.End;
                        filteredElements = relatedElements.Where(elem => elem.Model.Range.End == targetEndTime);
                    }
                    else if (_resizeType == AlignmentX.Left)
                    {
                        // 左端リサイズ: 同じStart時間の要素のみ
                        TimeSpan targetStartTime = viewModel.Model.Start;
                        filteredElements = relatedElements.Where(elem => elem.Model.Start == targetStartTime);
                    }
                    else
                    {
                        filteredElements = [viewModel];
                    }

                    bool clampToOriginal = GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength;

                    _resizeContexts = filteredElements.Select(elem =>
                    {
                        TimeSpan? originalDuration = null;
                        if (clampToOriginal
                            && elem.Model.HasOriginalDuration()
                            && elem.Model.TryGetOriginalDuration(out TimeSpan ts))
                        {
                            originalDuration = ts;
                        }

                        return new ElementResizeContext(
                            ViewModel: elem,
                            Before: elem.Model.GetBefore(elem.Model.ZIndex, elem.Model.Start),
                            After: elem.Model.GetAfter(elem.Model.ZIndex, elem.Model.Range.End),
                            RecordedStartTime: elem.Model.Start,
                            RecordedEndTime: elem.Model.Range.End,
                            LeftmostUpstreamStart: GetLeftmostUpstreamStart(viewModel.Scene, elem.Model),
                            OriginalDuration: originalDuration);
                    }).ToArray();

                    _pressed = true;
                    e.Handled = true;
                }
            }
        }

        private bool TryStartRollDrag(ElementViewModel viewModel, Point position, ElementView view)
        {
            if (_resizeType is not (AlignmentX.Left or AlignmentX.Right)) return false;

            Element model = viewModel.Model;
            Element? frontModel;
            Element? backModel;
            if (_resizeType == AlignmentX.Right)
            {
                frontModel = model;
                backModel = model.GetAfter(model.ZIndex, model.Range.End);
            }
            else
            {
                frontModel = model.GetBefore(model.ZIndex, model.Start);
                backModel = model;
            }

            if (frontModel is null || backModel is null) return false;
            if (frontModel.Range.End != backModel.Start) return false;

            ElementViewModel? frontVm = viewModel.Timeline.GetViewModelFor(frontModel);
            ElementViewModel? backVm = viewModel.Timeline.GetViewModelFor(backModel);
            if (frontVm is null || backVm is null) return false;

            _trimDrag = new TrimDragContext(
                Kind: TrimDragKind.Roll,
                Front: frontVm,
                Middle: null,
                Back: backVm,
                InitialFrontWidth: frontVm.Width.Value,
                InitialMiddleLeft: 0,
                InitialMiddleWidth: 0,
                InitialBackLeft: backVm.BorderMargin.Value.Left,
                InitialBackWidth: backVm.Width.Value,
                InitialPointerX: position.X);
            return true;
        }

        private bool TryStartSlideDrag(ElementViewModel viewModel, Point position, ElementView view)
        {
            Element middleModel = viewModel.Model;
            Element? frontModel = middleModel.GetBefore(middleModel.ZIndex, middleModel.Start);
            Element? backModel = middleModel.GetAfter(middleModel.ZIndex, middleModel.Range.End);
            if (frontModel is null || backModel is null) return false;
            if (frontModel.Range.End != middleModel.Start) return false;
            if (middleModel.Range.End != backModel.Start) return false;

            ElementViewModel? frontVm = viewModel.Timeline.GetViewModelFor(frontModel);
            ElementViewModel? middleVm = viewModel.Timeline.GetViewModelFor(middleModel);
            ElementViewModel? backVm = viewModel.Timeline.GetViewModelFor(backModel);
            if (frontVm is null || middleVm is null || backVm is null) return false;

            _trimDrag = new TrimDragContext(
                Kind: TrimDragKind.Slide,
                Front: frontVm,
                Middle: middleVm,
                Back: backVm,
                InitialFrontWidth: frontVm.Width.Value,
                InitialMiddleLeft: middleVm.BorderMargin.Value.Left,
                InitialMiddleWidth: middleVm.Width.Value,
                InitialBackLeft: backVm.BorderMargin.Value.Left,
                InitialBackWidth: backVm.Width.Value,
                InitialPointerX: position.X);
            return true;
        }

        private async void OnBorderPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_pressed)
            {
                _pressed = false;

                if (AssociatedObject is { ViewModel: { } viewModel })
                {
                    viewModel.Timeline.SnapBarPosition.Value = null;
                    e.Handled = true;

                    if (_trimDrag.Kind is not TrimDragKind.None)
                    {
                        TrimDragContext ctx = _trimDrag;
                        _trimDrag = default;

                        float scale = viewModel.Timeline.Options.Value.Scale;
                        int rate = viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
                        Point released = e.GetPosition(AssociatedObject);
                        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
                        TimeSpan delta = TrimDeltaCalculator.SnappedDelta(
                                ctx.InitialPointerX.PixelToTimeSpan(scale),
                                released.X.PixelToTimeSpan(scale),
                                t => AssociatedObject.RoundStartTime(t, scale, alt))
                            .RoundToRate(rate);
                        // RoundStartTime re-sets the snap guide line as a side effect; clear it.
                        viewModel.Timeline.SnapBarPosition.Value = null;

                        RestoreTrimDragVisuals(ctx);

                        if (delta != TimeSpan.Zero)
                        {
                            IElementResizeService resizeService = viewModel.Timeline.EditorContext
                                .GetRequiredService<IElementResizeService>();
                            if (ctx.Kind == TrimDragKind.Roll)
                            {
                                resizeService.Roll(viewModel.Scene, ctx.Front.Model, ctx.Back.Model, delta);
                            }
                            else
                            {
                                resizeService.Slide(viewModel.Scene, ctx.Front.Model, ctx.Middle!.Model, ctx.Back.Model, delta);
                            }
                        }

                        return;
                    }

                    bool ripple = viewModel.Timeline.IsRippleEnabled.Value
                        && _resizeType is AlignmentX.Right or AlignmentX.Left;
                    bool leftEdge = _resizeType == AlignmentX.Left;

                    if (_resizeContexts.Length == 1)
                    {
                        await viewModel.SubmitViewModelChanges(ripple, leftEdge);
                    }
                    else if (_resizeContexts.Length > 1)
                    {
                        var animations = _resizeContexts
                            .Select(x => (ViewModel: x.ViewModel, Context: x.ViewModel.PrepareAnimation()))
                            .ToArray();

                        float scale = viewModel.Timeline.Options.Value.Scale;
                        int rate = viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;

                        var requests = new ElementResizeRequest[_resizeContexts.Length];
                        for (int i = 0; i < _resizeContexts.Length; i++)
                        {
                            ElementResizeContext ctx = _resizeContexts[i];
                            TimeSpan roundedStart = ctx.ViewModel.BorderMargin.Value.Left.PixelToTimeSpan(scale).RoundToRate(rate);
                            TimeSpan roundedLength = ctx.ViewModel.Width.Value.PixelToTimeSpan(scale).RoundToRate(rate);
                            (TimeSpan newStart, TimeSpan newLength) = ripple
                                ? ElementViewModel.ResolveRippleResizeBounds(
                                    leftEdge, roundedStart, roundedLength, ctx.RecordedStartTime, ctx.RecordedEndTime)
                                : (roundedStart, roundedLength);
                            int zindex = viewModel.Timeline.ToLayerNumber(ctx.ViewModel.Margin.Value);
                            requests[i] = new ElementResizeRequest(ctx.ViewModel.Model, newStart, newLength, zindex);
                        }

                        viewModel.Timeline.EditorContext
                            .GetRequiredService<IElementResizeService>()
                            .Resize(viewModel.Scene, requests, ripple);

                        foreach (var (item, context) in animations)
                        {
                            _ = item.AnimationRequest(context);
                        }
                    }
                }

                _resizeContexts = [];
            }
        }

        private static void RestoreTrimDragVisuals(TrimDragContext ctx)
        {
            ctx.Front.Width.Value = ctx.InitialFrontWidth;
            ctx.Back.BorderMargin.Value = new Thickness(ctx.InitialBackLeft, 0, 0, 0);
            ctx.Back.Width.Value = ctx.InitialBackWidth;
            if (ctx.Middle is { } middle)
            {
                middle.BorderMargin.Value = new Thickness(ctx.InitialMiddleLeft, 0, 0, 0);
                middle.Width.Value = ctx.InitialMiddleWidth;
            }
        }

        private void OnBorderPointerMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject is { border: { } border, ViewModel: { } viewModel } view)
            {
                if (viewModel.Timeline.IsRazorMode.Value)
                {
                    view.Cursor = Cursors.Cross;
                    _resizeType = AlignmentX.Center;
                    return;
                }

                if (!viewModel.IsEditable.Value)
                {
                    view.Cursor = null;
                    _resizeType = AlignmentX.Center;
                    return;
                }

                if (e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Alt))
                {
                    view.Cursor = null;
                    _resizeType = AlignmentX.Center;
                }
                else if (!_pressed)
                {
                    float scale = viewModel.Timeline.Options.Value.Scale;
                    int rate = viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
                    double minWidth = TimeSpan.FromSeconds(1d / rate).TimeToPixel(scale);

                    Point point = e.GetPosition(border);
                    double horizon = point.X;

                    double handleWidth = 10;
                    if (border.Width <= minWidth)
                    {
                        handleWidth = minWidth / 2;
                    }

                    // 左右 10px内 なら左右矢印
                    if (horizon < handleWidth)
                    {
                        view.Cursor = Cursors.SizeWestEast;
                        _resizeType = AlignmentX.Left;
                    }
                    else if (horizon > border.Bounds.Width - handleWidth)
                    {
                        view.Cursor = Cursors.SizeWestEast;
                        _resizeType = AlignmentX.Right;
                    }
                    else
                    {
                        view.Cursor = null;
                        _resizeType = AlignmentX.Center;
                    }
                }
            }
        }
    }

    private sealed class _MoveBehavior : Behavior<ElementView>
    {
        private static readonly ILogger s_logger = Log.CreateLogger<ElementView>();

        private bool _pressed;
        private bool _duplicateMode;
        private bool _isSlipDrag;
        private readonly List<Control> _ghosts = [];
        private IReadOnlyList<ElementViewModel> _relatedElements = [];
        private Point _start;

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject == null) return;

            AssociatedObject.AddHandler(PointerMovedEvent, OnPointerMoved);
            AssociatedObject.border.AddHandler(PointerPressedEvent, OnBorderPointerPressed);
            AssociatedObject.border.AddHandler(PointerReleasedEvent, OnBorderPointerReleased);
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject == null) return;

            if (AssociatedObject._timeline is { } timeline) RemoveGhosts(timeline);
            _relatedElements = [];

            AssociatedObject.RemoveHandler(PointerMovedEvent, OnPointerMoved);
            AssociatedObject.border.RemoveHandler(PointerPressedEvent, OnBorderPointerPressed);
            AssociatedObject.border.RemoveHandler(PointerReleasedEvent, OnBorderPointerReleased);
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject is { ViewModel: { } viewModel, _timeline: { } timeline } view && _pressed)
            {
                Point point = e.GetPosition(view);
                float scale = viewModel.Timeline.Options.Value.Scale;
                TimeSpan pointerFrame = point.X.PixelToTimeSpan(scale);

                pointerFrame = view.RoundStartTime(pointerFrame, scale, e.KeyModifiers.HasFlag(KeyModifiers.Alt));

                if (_isSlipDrag)
                {
                    // Slip shifts the media window, not element geometry — there is no
                    // per-frame geometry preview to apply here. The Player overlay shows
                    // the media window shift; the service runs once on release.
                    e.Handled = true;
                    return;
                }

                TimeSpan newframe = pointerFrame - _start.X.PixelToTimeSpan(scale);

                newframe = TimeSpan.FromTicks(Math.Max(newframe.Ticks, TimeSpan.Zero.Ticks));

                var newTop = Math.Max(e.GetPosition(timeline.TimelinePanel).Y - _start.Y, 0);
                var newLeft = newframe.TimeToPixel(scale);
                var deltaTop = newTop - viewModel.Margin.Value.Top;
                var deltaLeft = newLeft - viewModel.BorderMargin.Value.Left;

                viewModel.Margin.Value = new(0, newTop, 0, 0);
                viewModel.BorderMargin.Value = new Thickness(newLeft, 0, 0, 0);

                foreach (ElementViewModel item in _relatedElements)
                {
                    if (item == viewModel) continue;
                    item.Margin.Value = new(0, item.Margin.Value.Top + deltaTop, 0, 0);
                    item.BorderMargin.Value = new(item.BorderMargin.Value.Left + deltaLeft, 0, 0, 0);
                }

                if (_duplicateMode && _ghosts.Count == 0)
                {
                    int rate = viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
                    TimeSpan minFrame = TimeSpan.FromSeconds(1d / rate);
                    TimeSpan modelStart = viewModel.BorderMargin.Value.Left.PixelToTimeSpan(scale).RoundToRate(rate);
                    TimeSpan deltaStart = modelStart - viewModel.Model.Start;
                    int modelIndex = viewModel.Timeline.ToLayerNumber(viewModel.Margin.Value);
                    int deltaIndex = modelIndex - viewModel.Model.ZIndex;
                    if (Math.Abs(deltaStart.Ticks) >= minFrame.Ticks || deltaIndex != 0)
                    {
                        SpawnGhostsForRelatedElements(_relatedElements, timeline);
                    }
                }

                e.Handled = true;
            }
        }

        private void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is { _timeline: { } timeline, border: { }, ViewModel: { } viewModel } view)
            {
                if (viewModel.Timeline.IsRazorMode.Value)
                {
                    return;
                }

                if (!viewModel.IsEditable.Value)
                {
                    return;
                }

                PointerPoint point = e.GetCurrentPoint(view.border);
                if (point.Properties.IsLeftButtonPressed
                    && (view.Cursor == Cursors.Arrow || view.Cursor == null))
                {
                    if (viewModel.Timeline.IsSlipMode.Value)
                    {
                        _pressed = true;
                        _isSlipDrag = true;
                        _start = e.GetPosition(view);
                        e.Handled = true;
                        return;
                    }

                    if (viewModel.Timeline.IsRollMode.Value || viewModel.Timeline.IsSlideMode.Value)
                    {
                        e.Handled = true;
                        return;
                    }

                    _pressed = true;
                    _duplicateMode = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
                    _relatedElements = viewModel.GetGroupOrSelectedElements()
                        .Where(el => el.IsEditable.Value)
                        .ToArray();
                    // Defensive: clear any ghosts orphaned by a prior Released early-return.
                    RemoveGhosts(timeline);
                    _start = point.Position;
                    e.Handled = true;
                }
            }
        }

        private void CommitSlipDrag(ElementView view, ElementViewModel viewModel, PointerReleasedEventArgs e)
        {
            float scale = viewModel.Timeline.Options.Value.Scale;
            Point released = e.GetPosition(view);
            bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            TimeSpan delta = TrimDeltaCalculator.SnappedDelta(
                _start.X.PixelToTimeSpan(scale),
                released.X.PixelToTimeSpan(scale),
                t => view.RoundStartTime(t, scale, alt));
            // RoundStartTime re-sets the snap guide line as a side effect; clear it.
            viewModel.Timeline.SnapBarPosition.Value = null;
            if (delta != TimeSpan.Zero)
            {
                viewModel.Timeline.EditorContext
                    .GetRequiredService<IElementSlipService>()
                    .Slip(viewModel.Model, delta);
            }
        }

        // Snap the visual state back to the model when AnimationRequest aborts midway,
        // otherwise margins/width stay frozen at the dragged position.
        private static void ForceRestoreVisualToModel(IEnumerable<ElementViewModel> targets)
        {
            foreach (ElementViewModel vm in targets)
            {
                // Never let a per-element NRE escape — we're on the async void handler path.
                try
                {
                    float scale = vm.Timeline.Options.Value.Scale;
                    vm.BorderMargin.Value = new Thickness(vm.Model.Start.TimeToPixel(scale), 0, 0, 0);
                    vm.Margin.Value = new Thickness(0, vm.Timeline.CalculateLayerTop(vm.Model.ZIndex), 0, 0);
                    vm.Width.Value = vm.Model.Length.TimeToPixel(scale);
                }
                catch (Exception ex)
                {
                    s_logger.LogWarning(ex, "Failed to restore visual state to model for element {Id}.", vm.Model.Id);
                }
            }
        }

        private async void OnBorderPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_pressed) return;

            _pressed = false;
            bool duplicate = _duplicateMode;
            bool ghostShown = _ghosts.Count > 0;
            _duplicateMode = false;
            IReadOnlyList<ElementViewModel> relatedElements = _relatedElements;
            _relatedElements = [];

            if (AssociatedObject is not { ViewModel: { } viewModel, _timeline: { } timeline } view) return;

            RemoveGhosts(timeline);

            viewModel.Timeline.SnapBarPosition.Value = null;
            e.Handled = true;

            if (_isSlipDrag)
            {
                _isSlipDrag = false;
                CommitSlipDrag(view, viewModel, e);
                return;
            }

            var elems = relatedElements.Select(x => x.Model).ToArray();

            float scale = viewModel.Timeline.Options.Value.Scale;
            int rate = viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
            TimeSpan newStart = viewModel.BorderMargin.Value.Left.PixelToTimeSpan(scale).RoundToRate(rate);
            TimeSpan deltaStart = newStart - viewModel.Model.Start;
            int newIndex = viewModel.Timeline.ToLayerNumber(viewModel.Margin.Value);
            int deltaIndex = newIndex - viewModel.Model.ZIndex;

            if (duplicate && !ghostShown)
            {
                // PointerMoved may have shifted the visual already; snap it back.
                s_logger.LogDebug(
                    "Alt+drag duplicate cancelled below threshold (deltaStart={DeltaStart}, deltaIndex={DeltaIndex}).",
                    deltaStart, deltaIndex);
                ForceRestoreVisualToModel(relatedElements);
                return;
            }

            if (!duplicate && elems.Length == 1)
            {
                // SubmitViewModelChanges writes Scene.MoveChild directly, so it
                // bypasses ElementMoveService's locked-destination refusal —
                // mirror that guard here for the single-clip drag path.
                if (viewModel.Scene.IsLayerLocked(newIndex))
                {
                    ForceRestoreVisualToModel(relatedElements);
                    return;
                }

                await viewModel.SubmitViewModelChanges();
                return;
            }

            if (elems.Length == 0) return;

            var animations = relatedElements
                .Select(x => (ViewModel: x, Context: x.PrepareAnimation()))
                .ToArray();

            IElementMoveService moveService = viewModel.Timeline.EditorContext
                .GetRequiredService<IElementMoveService>();
            ElementMoveOutcome outcome;
            try
            {
                outcome = duplicate
                    ? moveService.DuplicateOrMove(viewModel.Scene, elems, deltaStart, deltaIndex)
                    : moveService.Move(viewModel.Scene, elems, deltaStart, deltaIndex);
            }
            catch (Exception ex)
            {
                ForceRestoreVisualToModel(animations.Select(a => a.ViewModel));
                s_logger.LogError(ex, "Element move/duplicate failed.");
                NotificationService.ShowError(Strings.Duplicate_Failed, Strings.Duplicate_FallbackFailed);
                return;
            }

            switch (outcome)
            {
                case ElementMoveOutcome.DuplicateOverlapsSource:
                    ForceRestoreVisualToModel(animations.Select(a => a.ViewModel));
                    return;
                case ElementMoveOutcome.FellBackToMove:
                    NotificationService.ShowWarning(Strings.Duplicate_Failed, Strings.Duplicate_FallbackToMove);
                    break;
                case ElementMoveOutcome.None:
                    // Zero net delta (sub-frame drag), but OnPointerMoved already shifted
                    // Margin/BorderMargin; snap visuals back so clips aren't left offset.
                    ForceRestoreVisualToModel(animations.Select(a => a.ViewModel));
                    return;
            }

            try
            {
                // Must await: a fire-and-forget AnimationRequest swallows exceptions
                // and leaves the visual stuck at the dragged position.
                await Task.WhenAll(animations.Select(a => a.ViewModel.AnimationRequest(a.Context)));
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "Animation failed after duplicate/move; snapping visuals to model.");
                ForceRestoreVisualToModel(animations.Select(a => a.ViewModel));
            }
        }

        private void SpawnGhostsForRelatedElements(
            IReadOnlyList<ElementViewModel> related,
            TimelineTabView timeline)
        {
            foreach (ElementViewModel vm in related)
            {
                float scale = vm.Timeline.Options.Value.Scale;
                double left = vm.Model.Start.TimeToPixel(scale);
                double top = vm.Timeline.CalculateLayerTop(vm.Model.ZIndex);
                double width = vm.Model.Length.TimeToPixel(scale);

                var ghost = new Border
                {
                    Background = new ImmutableSolidColorBrush(vm.Color.Value),
                    Opacity = 0.4,
                    Width = width,
                    Height = FrameNumberHelper.LayerHeight,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(left, top, 0, 0),
                    IsHitTestVisible = false,
                };
                timeline.TimelinePanel.Children.Add(ghost);
                _ghosts.Add(ghost);
            }
        }

        private void RemoveGhosts(TimelineTabView timeline)
        {
            if (_ghosts.Count == 0) return;
            foreach (Control ghost in _ghosts)
            {
                timeline.TimelinePanel.Children.Remove(ghost);
            }
            _ghosts.Clear();
        }
    }

    private sealed class _SelectBehavior : Behavior<ElementView>
    {
        private bool _pressedWithModifier;
        private Thickness _snapshot;

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject == null) return;

            AssociatedObject.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            AssociatedObject.border.AddHandler(PointerPressedEvent, OnBorderPointerPressed);
            AssociatedObject.border.AddHandler(PointerReleasedEvent, OnBorderPointerReleased);
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject == null) return;

            AssociatedObject.RemoveHandler(PointerPressedEvent, OnPointerPressed);
            AssociatedObject.border.RemoveHandler(PointerPressedEvent, OnBorderPointerPressed);
            AssociatedObject.border.RemoveHandler(PointerReleasedEvent, OnBorderPointerReleased);
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is not { } obj) return;

            if (!obj.textBox.IsFocused)
            {
                obj.Focus();
            }
        }

        private void Select(ElementView obj, TimelineTabViewModel timeline)
        {
            var selection = timeline.EditorContext.GetRequiredService<IEditorSelection>();
            selection.SelectedObject.Value = obj.ViewModel.Model;

            timeline.ClearSelected();
            timeline.SelectElement(obj.ViewModel);
        }

        private bool IsSelected(ElementView obj, TimelineTabViewModel timeline)
        {
            var selection = timeline.EditorContext.GetRequiredService<IEditorSelection>();
            return selection.SelectedObject.Value == obj.ViewModel.Model
                || timeline.SelectedElements.Contains(obj.ViewModel);
        }

        private void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is { _timeline.ViewModel: { } timelineVm } obj)
            {
                if (timelineVm.IsRazorMode.Value && e.GetCurrentPoint(obj.border).Properties.IsLeftButtonPressed)
                {
                    if (obj.ViewModel is { } elementVm && elementVm.IsEditable.Value)
                    {
                        PointerPoint pt = e.GetCurrentPoint(obj.border);
                        float scale = timelineVm.Options.Value.Scale;
                        TimeSpan clickedTime = elementVm.Model.Start + pt.Position.X.PixelToTimeSpan(scale);
                        elementVm.SplitAt(clickedTime);
                    }

                    e.Handled = true;
                    return;
                }

                PointerPoint point = e.GetCurrentPoint(obj.border);
                if (point.Properties.IsLeftButtonPressed)
                {
                    if (e.ClickCount == 2)
                    {
                        if (obj.ViewModel is { IsEditable.Value: true })
                        {
                            obj.textBlock.IsVisible = false;
                            obj.textBox.IsVisible = true;
                            obj.textBox.SelectAll();
                            obj.textBox.Focus();
                        }
                    }
                    else
                    {
                        if (e.KeyModifiers is KeyModifiers.None or KeyModifiers.Alt)
                        {
                            Select(obj, obj._timeline.ViewModel);
                        }
                        else
                        {
                            Thickness margin = obj.ViewModel.Margin.Value;
                            Thickness borderMargin = obj.ViewModel.BorderMargin.Value;
                            _snapshot = new Thickness(borderMargin.Left, margin.Top, 0, 0);
                            _pressedWithModifier = true;
                        }
                    }
                }
                else if (point.Properties.IsRightButtonPressed)
                {
                    if (!IsSelected(obj, obj._timeline.ViewModel))
                    {
                        Select(obj, obj._timeline.ViewModel);
                    }
                }
            }
        }

        private void OnBorderPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (AssociatedObject is { _timeline.ViewModel: not null } obj)
            {
                if (_pressedWithModifier)
                {
                    Thickness margin = obj.ViewModel.Margin.Value;
                    Thickness borderMargin = obj.ViewModel.BorderMargin.Value;
                    // ReSharper disable CompareOfFloatsByEqualityOperator
                    if (borderMargin.Left == _snapshot.Left
                        && margin.Top == _snapshot.Top)
                    {
                        obj.ViewModel.Timeline.SwitchSelectedElement(obj.ViewModel);
                    }
                    // ReSharper restore CompareOfFloatsByEqualityOperator

                    _pressedWithModifier = false;
                }
            }
        }
    }
}
