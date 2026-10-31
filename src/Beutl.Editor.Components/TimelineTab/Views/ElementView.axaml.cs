using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.SilenceSplitTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Transitions;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings.Extensions;
using Setter = Avalonia.Styling.Setter;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class ElementView : UserControl
{
    private static readonly ILogger s_logger = Log.CreateLogger<ElementView>();
    private readonly CompositeDisposable _disposables = [];
    private TimelineTabView? _timeline;
    private MediaTime _pointerPosition;
    private static ColorPickerFlyout? s_colorPickerFlyout;
    private _ResizeBehavior? _resizeBehavior;
    // The width of an element's edge that counts as its boundary with the element beside it; the same zone
    // the edge resize uses.
    private const double BoundaryHitWidth = 10;
    private ElementEdge? _draggingTransitionEdge;
    private double _transitionDragStartX;
    private MediaTime _transitionDragStartDuration;
    private MediaTime? _lastSnapTarget;
    private IPointer? _interactionPointer;
    private int _keepRealized;

    internal bool KeepRealized => _keepRealized > 0 || IsKeyboardFocusWithin
        || border.ContextFlyout?.IsOpen == true
        || _interactionPointer?.Captured is Visual captured
            && (ReferenceEquals(captured, this) || this.IsVisualAncestorOf(captured));

    public ElementView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, (_, e) => _interactionPointer = e.Pointer, RoutingStrategies.Tunnel, true);
        LostFocus += (_, _) => _timeline?.InvalidateElementViewport();
        TimelineEditingModeMenu.Populate(editingModeMenu, "Timeline.");

        var cm = AppHelper.GetContextCommandManager?.Invoke();
        cm?.Attach(this, TimelineTabExtension.Instance);
        (border.ContextFlyout as FAMenuFlyout)!.Opening += OnContextFlyoutOpening;
        textBox.LostFocus += OnTextBoxLostFocus;
        foreach (Border handle in (ReadOnlySpan<Border>)[enterTransitionHandle, exitTransitionHandle])
        {
            handle.AddHandler(PointerPressedEvent, OnTransitionHandlePressed);
            handle.AddHandler(PointerMovedEvent, OnTransitionHandleMoved);
            handle.AddHandler(PointerReleasedEvent, OnTransitionHandleReleased);
            handle.AddHandler(PointerCaptureLostEvent, OnTransitionHandleCaptureLost);
        }
        this.SubscribeDataContextChange<ElementViewModel>(OnDataContextAttached, OnDataContextDetached);
    }

    private ElementViewModel ViewModel => (ElementViewModel)DataContext!;

    // Keeps the view realized while it is scrolled away, until the scope is disposed.
    private IDisposable KeepRealizedScope()
    {
        _keepRealized++;
        return Disposable.Create(() =>
        {
            _keepRealized--;
            _timeline?.InvalidateElementViewport();
        });
    }

    // A flyout the view has just shown is anchored to it, so the view stays realized until the flyout closes.
    private void KeepRealizedWhileOpen(FlyoutBase flyout)
    {
        if (!flyout.IsOpen) return;

        IDisposable scope = KeepRealizedScope();
        flyout.Closed += OnClosed;

        void OnClosed(object? sender, EventArgs e)
        {
            flyout.Closed -= OnClosed;
            scope.Dispose();
        }
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
        autoSplitBySilence.IsEnabled = editable && SilenceSplitViewModel.CanAnalyze(viewModel.Scene, viewModel.Model);
        cut.IsEnabled = editable;
        // Recovery protection makes a clip read-only but still lets it be removed.
        delete.IsEnabled = !viewModel.IsLockedByUser.Value;
        finishEditingAnimation.IsEnabled = editable;
        rename.IsEnabled = editable;
        enableElement.IsEnabled = editable;
        changeColor.IsEnabled = editable;
        // With the layer locked, clearing the element flag has no visible effect
        // (IsEditable stays false), so the toggle would read as broken. A clip that recovery
        // protects cannot be locked either (ElementAttributeService.SetLocked refuses it).
        lockElement.IsEnabled = viewModel.CanToggleLock.Value
                                && viewModel.LayerHeader.Value?.IsLocked.Value != true;
        PopulateTrackMatteMenu(viewModel);
        PopulateTransitionMenu(viewModel, editable);
    }

    private void AutoSplitBySilence_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ElementViewModel { IsEditable.Value: true } viewModel)
            SilenceSplitTabViewModel.Open(viewModel.Timeline.EditorContext, viewModel.Model);
    }

    // One item per edge, which opens its transition in the property tab, adding the default one first
    // when there is none; its type and properties are changed there.
    private void PopulateTransitionMenu(ElementViewModel viewModel, bool editable)
    {
        transitionMenu.Items.Clear();
        transitionMenu.Items.Add(CreateTransitionEdgeItem(viewModel, ElementEdge.Start, Strings.EnterTransition, editable));
        transitionMenu.Items.Add(CreateTransitionEdgeItem(viewModel, ElementEdge.End, Strings.ExitTransition, editable));
    }

    private FAMenuFlyoutItem CreateTransitionEdgeItem(
        ElementViewModel viewModel, ElementEdge edge, string text, bool editable)
    {
        var item = new FAMenuFlyoutItem
        {
            Text = text,
            IsEnabled = editable || viewModel.GetTransitionType(edge) != null,
        };
        item.Click += (_, _) => (DataContext as ElementViewModel)?.OpenTransition(edge);
        return item;
    }

    private void OnTransitionHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        ElementEdge edge = ReferenceEquals(sender, enterTransitionHandle) ? ElementEdge.Start : ElementEdge.End;
        if (DataContext is not ElementViewModel { IsEditable.Value: true } viewModel
            || viewModel.Timeline.IsRazorMode.Value
            || viewModel.GetOwnTransitionDuration(edge) is not { } duration
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _draggingTransitionEdge = edge;
        _transitionDragStartX = e.GetPosition(border).X;
        _transitionDragStartDuration = duration;
        e.Pointer.Capture((IInputElement?)sender);
        e.Handled = true;
    }

    private void OnTransitionHandleMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingTransitionEdge is not { } edge || DataContext is not ElementViewModel viewModel) return;

        viewModel.PreviewTransitionDuration(edge, GetDraggedTransitionDuration(viewModel, edge, e));
        e.Handled = true;
    }

    private void OnTransitionHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingTransitionEdge is not { } edge || DataContext is not ElementViewModel viewModel) return;

        _draggingTransitionEdge = null;
        MediaTime duration = GetDraggedTransitionDuration(viewModel, edge, e);
        e.Pointer.Capture(null);
        viewModel.CommitTransitionDuration(edge, duration);
        e.Handled = true;
    }

    private void OnTransitionHandleCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_draggingTransitionEdge is not { } edge) return;

        _draggingTransitionEdge = null;
        (DataContext as ElementViewModel)?.PreviewTransitionDuration(edge, null);
    }

    // The side grows by however far the pointer has moved since the press, away from its edge, snapped to
    // frames and kept within the element. A press without a drag keeps the duration as it is, even when it
    // is not a whole number of frames.
    private MediaTime GetDraggedTransitionDuration(ElementViewModel viewModel, ElementEdge edge, PointerEventArgs e)
    {
        double delta = e.GetPosition(border).X - _transitionDragStartX;
        if (delta == 0) return _transitionDragStartDuration;

        float scale = viewModel.Timeline.Options.Value.Scale;
        Rational rate = SceneTimeRangeService.GetFrameRate(viewModel.Scene);
        if (edge == ElementEdge.End) delta = -delta;

        MediaTime duration = (_transitionDragStartDuration + delta.PixelToTime(scale)).RoundToFrame(rate, MidpointRounding.AwayFromZero);
        if (duration < MediaTime.Zero) duration = MediaTime.Zero;
        if (duration > viewModel.Model.Length.Value) duration = viewModel.Model.Length.Value;
        return duration;
    }

    // A double-click on a transition part edits it; one on an edge another element meets adds the default
    // transition there, or edits the one already there. Returns whether the click was taken.
    internal bool HandleTransitionDoubleClick(PointerPressedEventArgs e)
    {
        if (DataContext is not ElementViewModel viewModel) return false;

        if (IsWithin(e, enterTransitionPart))
        {
            viewModel.EditTransition(ElementEdge.Start);
            return true;
        }

        if (IsWithin(e, exitTransitionPart))
        {
            viewModel.EditTransition(ElementEdge.End);
            return true;
        }

        double x = e.GetPosition(border).X;
        ElementEdge? edge = x < BoundaryHitWidth ? ElementEdge.Start
            : x > border.Bounds.Width - BoundaryHitWidth ? ElementEdge.End
            : null;
        if (edge is not { } boundaryEdge || !viewModel.HasTransitionPartner(boundaryEdge)) return false;

        if (viewModel.GetTransitionType(boundaryEdge) != null)
            viewModel.EditTransition(boundaryEdge);
        else
            viewModel.ApplyTransition(boundaryEdge, typeof(CrossDissolveTransition));
        return true;
    }

    private static bool IsWithin(RoutedEventArgs e, Visual part)
    {
        return part.IsVisible
               && e.Source is Visual source
               && (source == part || part.IsVisualAncestorOf(source));
    }

    private void LockElement_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ElementViewModel viewModel) return;
        Element model = viewModel.Model;
        viewModel.Timeline.EditorContext.GetRequiredService<IElementAttributeService>()
            .SetLocked(model, !model.IsLocked.Value);
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

        obj.SetTimelineViewAttached(false);
        thumbnailStrip.ClearThumbnails();
        waveformControl.ClearChunks();
        _interactionPointer = null;

        _disposables.Clear();
    }

    private void OnDataContextAttached(ElementViewModel obj)
    {
        obj.InitializeViewState();
        obj.AnimationRequested = async (args, token) =>
        {
            using (KeepRealizedScope())
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var animation1 = TimelineSettleAnimation.Create(
                        [new Setter(MarginProperty, border.Margin), new Setter(WidthProperty, border.Width)],
                        [new Setter(MarginProperty, args.BorderMargin), new Setter(WidthProperty, args.Width)]);
                    var animation2 = TimelineSettleAnimation.Create(
                        [new Setter(MarginProperty, obj.Margin.Value)],
                        [new Setter(MarginProperty, args.Margin)]);

                    Task task1 = animation1.RunAsync(border, token);
                    Task task2 = animation2.RunAsync(this, token);
                    await Task.WhenAll(task1, task2);
                });
            }
        };
        obj.RenameRequested = () => Rename_Click(null, null!);
        obj.GetClickedTime = () => _pointerPosition;

        obj.GetMissingThumbnailIndices = thumbnailStrip.GetMissingIndices;
        thumbnailStrip.VisibleRangeChanged += obj.OnVisibleRangeChanged;

        obj.IsSelected
            .ObserveOnUIDispatcher()
            .Subscribe(v =>
            {
                ZIndex = v ? 5 : 0;
                if (!v) _timeline?.InvalidateElementViewport();
            })
            .DisposeWith(_disposables);

        obj.ThumbnailReady += OnThumbnailReady;
        obj.ThumbnailsClear += OnThumbnailsClear;
        obj.WaveformChunkReady += OnWaveformChunkReady;
        obj.WaveformClear += OnWaveformClear;
        obj.SetTimelineViewAttached(true);
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
        _pointerPosition = point.X.PixelToTime(scale);
    }

    private void EnableElementClick(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsEditable.Value) return;

        Element model = ViewModel.Model;
        ViewModel.Timeline.EditorContext.GetRequiredService<IElementAttributeService>()
            .SetEnabled(model, !model.IsEnabled.Value);
    }

    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        textBlock.IsVisible = true;
        textBox.IsVisible = false;
    }

    private void Rename_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ElementViewModel { IsEditable.Value: true }) return;

        BeginRename();
    }

    private void BeginRename()
    {
        textBlock.IsVisible = false;
        textBox.IsVisible = true;
        textBox.SelectAll();
        textBox.Focus();
    }

    private void SaveAsTemplate_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ElementViewModel viewModel) return;

        KeepRealizedWhileOpen(TemplateSaving.ShowSaveFlyout(
            this, viewModel.Model, TimelineTabExtension.GetDefaultTemplateName(viewModel.Model), showAtPointer: true));
    }

    private void ChangeColor_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ElementViewModel { IsEditable.Value: true } viewModel) return;

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

        s_colorPickerFlyout.ShowAt(border);
        KeepRealizedWhileOpen(s_colorPickerFlyout);
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

    private SnapResult SnapTime(MediaTime time, float scale, bool disabled, int? sameZIndex = null)
    {
        Element model = ViewModel.Model;
        TimelineTabViewModel timeline = ViewModel.Timeline;

        if (disabled || !timeline.IsSnapEnabled.Value)
        {
            return new SnapResult(time, false);
        }

        IEditorClock clock = timeline.EditorContext.GetRequiredService<IEditorClock>();
        IEnumerable<MediaTime> candidates = SnapHelper
            .CollectElementCandidates(timeline.Scene.Children, model, sameZIndex)
            .Concat(SnapHelper.CollectSceneCandidates(timeline.Scene, clock.CurrentTime.Value));

        return SnapHelper.Snap(time, candidates, scale);
    }

    // Publish only the final preview, after any trim constraints have been applied.
    // Intermediate calculations and commit/undo must not produce feedback.
    private void UpdateSnapFeedback(MediaTime? target, float scale, ulong timestamp)
    {
        ViewModel.Timeline.SnapBarPosition.Value = target?.TimeToPixel(scale);
        MediaTime? previous = _lastSnapTarget;
        _lastSnapTarget = target;
        if (target.HasValue && target != previous)
        {
            NativeHapticFeedback.PerformAlignment(this, timestamp);
        }
    }

    private void ClearSnapFeedback()
    {
        _lastSnapTarget = null;
        ViewModel.Timeline.SnapBarPosition.Value = null;
    }

    // The pressed clip's group or selection and the clip itself. Locked members stay: the Roll/Slide check needs
    // them, and the slip service drops them itself.
    private static Element[] CollectTrimMembers(ElementViewModel viewModel)
    {
        var members = viewModel.GetGroupOrSelectedElements()
            .Select(el => el.Model)
            .ToList();
        if (!members.Contains(viewModel.Model))
        {
            members.Add(viewModel.Model);
        }

        return [.. members];
    }
}
