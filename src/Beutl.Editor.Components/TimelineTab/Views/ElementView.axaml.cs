using Avalonia;
using Avalonia.Controls;
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

/*
 * 移動アニメーション中にUndoを行うと、
 * 表示される位置がUndo前になる。
 * 解決するには、オブジェクトがUndo/Redoしたかを追跡するAPIを追加して、
 * Undo/Redoがされた場合アニメーションをキャンセルする必要がある。
 */

public sealed partial class ElementView : UserControl
{
    private static readonly ILogger s_logger = Log.CreateLogger<ElementView>();
    private readonly CompositeDisposable _disposables = [];
    private TimelineTabView? _timeline;
    private TimeSpan _pointerPosition;
    private static ColorPickerFlyout? s_colorPickerFlyout;
    private _ResizeBehavior? _resizeBehavior;
    // The width of an element's edge that counts as its boundary with the element beside it; the same zone
    // the edge resize uses.
    private const double BoundaryHitWidth = 10;
    private ElementEdge? _draggingTransitionEdge;
    private double _transitionDragStartX;
    private TimeSpan _transitionDragStartDuration;

    public ElementView()
    {
        InitializeComponent();

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

    private static int FrameRateOf(ElementViewModel viewModel)
        => viewModel.Scene.FindHierarchicalParent<Project>().GetFrameRate();

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
        PopulateTransitionMenu(viewModel, editable);
    }

    private void PopulateTransitionMenu(ElementViewModel viewModel, bool editable)
    {
        transitionMenu.Items.Clear();
        transitionMenu.Items.Add(CreateTransitionEdgeMenu(viewModel, ElementEdge.Start, Strings.EnterTransition, editable));
        transitionMenu.Items.Add(CreateTransitionEdgeMenu(viewModel, ElementEdge.End, Strings.ExitTransition, editable));
    }

    private FAMenuFlyoutSubItem CreateTransitionEdgeMenu(
        ElementViewModel viewModel, ElementEdge edge, string header, bool editable)
    {
        var menu = new FAMenuFlyoutSubItem { Text = header };
        Type? current = viewModel.GetTransitionType(edge);
        foreach (Type type in ElementViewModel.GetTransitionTypes())
        {
            var item = new FAToggleMenuFlyoutItem
            {
                Text = ElementViewModel.GetTransitionName(type),
                IsChecked = type == current,
                IsEnabled = editable,
            };
            item.Click += (_, _) => (DataContext as ElementViewModel)?.ApplyTransition(edge, type);
            menu.Items.Add(item);
        }

        menu.Items.Add(new FAMenuFlyoutSeparator());

        var edit = new FAMenuFlyoutItem { Text = Strings.EditTransition, IsEnabled = current != null };
        edit.Click += (_, _) => (DataContext as ElementViewModel)?.EditTransition(edge);
        menu.Items.Add(edit);

        var remove = new FAMenuFlyoutItem
        {
            Text = Strings.RemoveTransition,
            IsEnabled = editable && current != null && !ElementTransitionEdits.HasLockedSideAcross(viewModel.Model, edge),
        };
        remove.Click += (_, _) => (DataContext as ElementViewModel)?.RemoveTransition(edge);
        menu.Items.Add(remove);
        return menu;
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
        TimeSpan duration = GetDraggedTransitionDuration(viewModel, edge, e);
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
    private TimeSpan GetDraggedTransitionDuration(ElementViewModel viewModel, ElementEdge edge, PointerEventArgs e)
    {
        double delta = e.GetPosition(border).X - _transitionDragStartX;
        if (delta == 0) return _transitionDragStartDuration;

        float scale = viewModel.Timeline.Options.Value.Scale;
        int rate = FrameRateOf(viewModel);
        if (edge == ElementEdge.End) delta = -delta;

        TimeSpan duration = (_transitionDragStartDuration + delta.PixelToTimeSpan(scale)).RoundToRate(rate);
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        if (duration > viewModel.Model.Length) duration = viewModel.Model.Length;
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

        string defaultName = !string.IsNullOrWhiteSpace(viewModel.Model.Name)
            ? viewModel.Model.Name
            : TypeDisplayHelpers.GetLocalizedName(viewModel.Model.GetType());
        string uniqueName = ObjectTemplateService.Instance.GetUniqueName(defaultName);

        var flyout = new SaveAsTemplateFlyout { Text = uniqueName };
        flyout.Confirmed += async (_, name) =>
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                try
                {
                    if (await ObjectTemplateService.Instance.AddFromInstanceAsync(viewModel.Model, name) is null)
                        Beutl.Services.NotificationService.ShowError(Beutl.Language.Strings.SaveAsTemplate, Beutl.Language.MessageStrings.OperationFailed);
                }
                catch (Exception)
                {
                    Beutl.Services.NotificationService.ShowError(Beutl.Language.Strings.SaveAsTemplate, Beutl.Language.MessageStrings.OperationFailed);
                }
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

    // The model-level member set a Slip/Roll/Slide gesture acts on: the pressed clip's
    // group or multi-selection, always including the pressed clip itself
    // (GetGroupOrSelectedElements returns empty when it is neither grouped nor selected).
    // Locked members are deliberately NOT filtered here: TrimGroupCollector needs to see
    // them to apply its all-or-nothing locked-participant rule for Roll/Slide, and the
    // slip service drops them itself at the mutation boundary.
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
