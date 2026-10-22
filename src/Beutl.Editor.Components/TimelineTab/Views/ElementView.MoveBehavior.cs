using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Immutable;
using Avalonia.Xaml.Interactivity;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.JsonConverters;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class ElementView
{
    private sealed class _MoveBehavior : Behavior<ElementView>
    {
        private bool _pressed;
        private bool _duplicateMode;
        private bool _isSlipDrag;
        private Element[] _slipTargets = [];
        private readonly List<Control> _ghosts = [];
        private IReadOnlyList<ElementViewModel> _relatedElements = [];
        private Point _start;
        private Point _lastPosition;
        private bool _lastAlt;
        private DragSession? _drag;

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject == null) return;

            AssociatedObject.AddHandler(PointerMovedEvent, OnPointerMoved);
            AssociatedObject.border.AddHandler(PointerPressedEvent, OnBorderPointerPressed);
            AssociatedObject.border.AddHandler(PointerReleasedEvent, OnBorderPointerReleased);
            AssociatedObject.border.AddHandler(PointerCaptureLostEvent, OnBorderPointerCaptureLost);
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject == null) return;

            _drag?.Confirm();
            if (AssociatedObject._timeline is { } timeline) RemoveGhosts(timeline);
            _relatedElements = [];

            AssociatedObject.RemoveHandler(PointerMovedEvent, OnPointerMoved);
            AssociatedObject.border.RemoveHandler(PointerPressedEvent, OnBorderPointerPressed);
            AssociatedObject.border.RemoveHandler(PointerReleasedEvent, OnBorderPointerReleased);
            AssociatedObject.border.RemoveHandler(PointerCaptureLostEvent, OnBorderPointerCaptureLost);
        }

        // A drag in progress confirms through its DragSession; a press that never moved has nothing to keep.
        private void OnBorderPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (_pressed && _drag is null)
                CancelDrag();
        }

        // Escape, Undo and a press that never moved restore the clips. Slip has no geometry preview to restore.
        private void CancelDrag()
        {
            _drag = null;
            _pressed = false;
            _isSlipDrag = false;
            _duplicateMode = false;
            _slipTargets = [];
            IReadOnlyList<ElementViewModel> relatedElements = _relatedElements;
            _relatedElements = [];
            if (AssociatedObject?._timeline is { } timeline) RemoveGhosts(timeline);
            ForceRestoreVisualToModel(relatedElements);
            if (AssociatedObject is { ViewModel: { } viewModel })
            {
                // Selection modifiers can leave the pressed clip out of the participant list.
                if (!relatedElements.Contains(viewModel))
                    ForceRestoreVisualToModel([viewModel]);
                viewModel.Timeline.SnapBarPosition.Value = null;
            }
        }

        // The first move opens the DragSession; null means another gesture holds history.
        private bool EnsureDrag(ElementView view, ElementViewModel viewModel)
        {
            _drag ??= DragSession.Begin(
                viewModel.Timeline.EditorContext, view.border, CommandNames.MoveElement, ConfirmDrag, CancelDrag);
            if (_drag is not null)
                return true;

            CancelDrag();
            return false;
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject is { ViewModel: { } viewModel, _timeline: { } timeline } view && _pressed)
            {
                if (!EnsureDrag(view, viewModel))
                    return;

                Point point = e.GetPosition(view);
                _lastPosition = point;
                _lastAlt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
                float scale = viewModel.Timeline.Options.Value.Scale;
                MediaTime pointerFrame = point.X.PixelToTime(scale);

                pointerFrame = view.RoundStartTime(pointerFrame, scale, _lastAlt);

                if (_isSlipDrag)
                {
                    // Slip shifts the media window, not element geometry — there is no
                    // per-frame geometry preview to apply here. The Player overlay shows
                    // the media window shift; the service runs once on release.
                    e.Handled = true;
                    return;
                }

                MediaTime newframe = pointerFrame - _start.X.PixelToTime(scale);

                if (newframe < MediaTime.Zero) newframe = MediaTime.Zero;

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
                    Rational rate = SceneTimeRangeService.GetFrameRate(viewModel.Scene);
                    MediaTime minFrame = SceneTimeRangeService.GetFrameDuration(viewModel.Scene);
                    MediaTime modelStart = viewModel.BorderMargin.Value.Left.PixelToTime(scale).RoundToFrame(rate, MidpointRounding.AwayFromZero);
                    // A start within a tick of a frame, as one written by hand can be, counts from that frame.
                    MediaTime deltaStart = modelStart - MediaTimeJsonConverter.SnapToFrame(viewModel.Model.Start, rate);
                    int modelIndex = viewModel.Timeline.ToLayerNumber(viewModel.Margin.Value);
                    int deltaIndex = modelIndex - viewModel.Model.ZIndex;
                    if (Math.Abs(deltaStart.Flicks) >= minFrame.Flicks || deltaIndex != 0)
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
                if (!point.Properties.IsLeftButtonPressed)
                {
                    return;
                }

                // Slip shifts the media window, not clip geometry, so it starts anywhere on the clip
                // — including the resize edge, where a plain resize would otherwise take over.
                // Selection-modifier clicks (Ctrl/Shift) belong to _SelectBehavior and must not
                // start a slip; Alt stays allowed as the snap-disable modifier, like other drags.
                if (viewModel.Timeline.IsSlipMode.Value)
                {
                    if (e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Alt))
                    {
                        return;
                    }

                    _pressed = true;
                    _isSlipDrag = true;
                    _slipTargets = CollectTrimMembers(viewModel);
                    _start = _lastPosition = e.GetPosition(view);
                    _lastAlt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
                    // PointerCaptureLost routes Direct, so move the capture to the border.
                    e.Pointer.Capture(view.border);
                    e.Handled = true;
                    return;
                }

                if (view.Cursor == Cursors.Arrow || view.Cursor == null)
                {
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

        private void CommitSlipDrag(ElementView view, ElementViewModel viewModel)
        {
            Element[] targets = _slipTargets;
            _slipTargets = [];

            float scale = viewModel.Timeline.Options.Value.Scale;
            Rational rate = SceneTimeRangeService.GetFrameRate(viewModel.Scene);
            bool alt = _lastAlt;
            MediaTime delta = TrimDeltaCalculator.SnappedDelta(
                    _start.X.PixelToTime(scale),
                    _lastPosition.X.PixelToTime(scale),
                    t => view.RoundStartTime(t, scale, alt))
                .RoundToFrame(rate, MidpointRounding.AwayFromZero);
            // RoundStartTime re-sets the snap guide line as a side effect; clear it.
            viewModel.Timeline.SnapBarPosition.Value = null;
            if (delta != MediaTime.Zero && targets.Length > 0)
            {
                viewModel.Timeline.EditorContext
                    .GetRequiredService<IElementSlipService>()
                    .Slip(viewModel.Scene, targets, delta);
            }
        }

        private void OnBorderPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_pressed) return;

            e.Handled = true;
            if (AssociatedObject is { } view)
            {
                _lastPosition = e.GetPosition(view);
                _lastAlt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            }

            if (_drag is { } drag)
                drag.Confirm();
            else
                ConfirmDrag();
        }

        // Release, capture loss, playback start and detach all keep the drag where it reached.
        private async void ConfirmDrag()
        {
            _drag = null;
            _pressed = false;
            bool duplicate = _duplicateMode;
            bool ghostShown = _ghosts.Count > 0;
            _duplicateMode = false;
            IReadOnlyList<ElementViewModel> relatedElements = _relatedElements;
            _relatedElements = [];

            if (AssociatedObject is not { ViewModel: { } viewModel, _timeline: { } timeline } view) return;

            RemoveGhosts(timeline);

            viewModel.Timeline.SnapBarPosition.Value = null;

            if (_isSlipDrag)
            {
                _isSlipDrag = false;
                CommitSlipDrag(view, viewModel);
                return;
            }

            var elems = relatedElements.Select(x => x.Model).ToArray();

            float scale = viewModel.Timeline.Options.Value.Scale;
            Rational rate = SceneTimeRangeService.GetFrameRate(viewModel.Scene);
            MediaTime newStart = viewModel.BorderMargin.Value.Left.PixelToTime(scale).RoundToFrame(rate, MidpointRounding.AwayFromZero);
            MediaTime deltaStart = newStart - viewModel.Model.Start;
            int newIndex = viewModel.Timeline.ToLayerNumber(viewModel.Margin.Value);
            int deltaIndex = newIndex - viewModel.Model.ZIndex;
            // Selection modifiers can leave the pressed clip out of the participant list; it stays where it was.
            bool pressedMoves = relatedElements.Contains(viewModel);
            if (!pressedMoves)
                ForceRestoreVisualToModel([viewModel]);

            if (duplicate && !ghostShown)
            {
                // PointerMoved may have shifted the visual already; snap it back.
                s_logger.LogDebug(
                    "Alt+drag duplicate cancelled below threshold (deltaStart={DeltaStart}, deltaIndex={DeltaIndex}).",
                    deltaStart, deltaIndex);
                ForceRestoreVisualToModel(relatedElements);
                return;
            }

            if (!duplicate && elems.Length == 1 && pressedMoves)
            {
                // IElementResizeService.Resize does not check the lock of the destination layer, so check it here.
                if (viewModel.Scene.IsLayerLocked(newIndex))
                {
                    ForceRestoreVisualToModel(relatedElements);
                    return;
                }

                await viewModel.SubmitViewModelChanges();
                return;
            }

            if (elems.Length == 0) return;

            await CommitGroupMoveAsync(viewModel, relatedElements, elems, duplicate, deltaStart, deltaIndex);
        }

        private static async Task CommitGroupMoveAsync(
            ElementViewModel viewModel,
            IReadOnlyList<ElementViewModel> relatedElements,
            Element[] elems,
            bool duplicate,
            MediaTime deltaStart,
            int deltaIndex)
        {
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
                ForceRestoreVisualToModel(relatedElements);
                s_logger.LogError(ex, "Element move/duplicate failed.");
                NotificationService.ShowError(Strings.Duplicate_Failed, Strings.Duplicate_FallbackFailed);
                return;
            }

            switch (outcome)
            {
                case ElementMoveOutcome.DuplicateOverlapsSource:
                    ForceRestoreVisualToModel(relatedElements);
                    return;
                case ElementMoveOutcome.FellBackToMove:
                    NotificationService.ShowWarning(Strings.Duplicate_Failed, Strings.Duplicate_FallbackToMove);
                    break;
                case ElementMoveOutcome.None:
                    // Zero net delta (sub-frame drag), but OnPointerMoved already shifted
                    // Margin/BorderMargin; snap visuals back so clips aren't left offset.
                    ForceRestoreVisualToModel(relatedElements);
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
                ForceRestoreVisualToModel(relatedElements);
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
}
