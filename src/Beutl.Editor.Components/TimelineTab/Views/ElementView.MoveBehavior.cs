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
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ExtensionGuard = Beutl.Api.Services.ExtensionGuard;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class ElementView
{
    private sealed class _MoveBehavior : Behavior<ElementView>
    {
        private bool _pressed;
        private bool _duplicateMode;
        private bool _isSlipDrag;
        private Element[] _slipTargets = [];
        private ElementViewModel[] _slipPreviewElements = [];
        private List<SlippableMedia.Target> _slipMediaTargets = [];
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

        // Escape, Undo and a press that never moved restore the clips and any slip thumbnail previews.
        private void CancelDrag()
        {
            _drag = null;
            _pressed = false;
            _isSlipDrag = false;
            _duplicateMode = false;
            _slipTargets = [];
            ElementViewModel[] slipPreviewElements = _slipPreviewElements;
            _slipPreviewElements = [];
            _slipMediaTargets = [];
            foreach (ElementViewModel vm in slipPreviewElements)
            {
                vm.EndMediaPreview(committed: false);
            }
            IReadOnlyList<ElementViewModel> relatedElements = _relatedElements;
            _relatedElements = [];
            if (AssociatedObject?._timeline is { } timeline) RemoveGhosts(timeline);
            ForceRestoreVisualToModel(relatedElements);
            if (AssociatedObject is { ViewModel: { } viewModel } view)
            {
                // Selection modifiers can leave the pressed clip out of the participant list.
                if (!relatedElements.Contains(viewModel))
                    ForceRestoreVisualToModel([viewModel]);
                view.ClearSnapFeedback();
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

                if (_isSlipDrag)
                {
                    Rational slipRate = SceneTimeRangeService.GetFrameRate(viewModel.Scene);
                    MediaTime pressTime = view.SnapTime(_start.X.PixelToTime(scale), scale, _lastAlt).Time;
                    SnapResult slipSnap = view.SnapTime(pointerFrame, scale, _lastAlt);
                    MediaTime requestedDelta = (slipSnap.Time - pressTime).RoundToFrame(slipRate, MidpointRounding.AwayFromZero);
                    MediaTime previewDelta = _slipMediaTargets.Count > 0
                        ? SlippableMedia.ClampSharedDelta(_slipMediaTargets, requestedDelta)
                        : MediaTime.Zero;
                    double offsetX = -previewDelta.TimeToPixel(scale);
                    foreach (ElementViewModel vm in _slipPreviewElements)
                    {
                        vm.UpdateMediaPreview(offsetX);
                    }

                    view.UpdateSnapFeedback(slipSnap.DidSnap && previewDelta == requestedDelta ? slipSnap.Time : null, scale, e.Timestamp);
                    e.Handled = true;
                    return;
                }

                Rational rate = SceneTimeRangeService.GetFrameRate(viewModel.Scene);
                // Snap the clip's start after removing the grab offset and rounding to the commit frame grid.
                MediaTime newStart = (pointerFrame - _start.X.PixelToTime(scale))
                    .RoundToFrame(rate, MidpointRounding.AwayFromZero);

                if (newStart < MediaTime.Zero) newStart = MediaTime.Zero;
                // A duplicate leaves the originals where they are, so their edges still count.
                SnapResult snap = view.SnapTime(newStart, scale, _lastAlt, moving: _duplicateMode ? null : _relatedElements);
                newStart = snap.Time;

                var newTop = Math.Max(e.GetPosition(timeline.TimelinePanel).Y - _start.Y, 0);
                var newLeft = newStart.TimeToPixel(scale);
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

                view.UpdateSnapFeedback(snap.DidSnap ? snap.Time : null, scale, e.Timestamp);

                if (_duplicateMode && _ghosts.Count == 0)
                {
                    MediaTime minFrame = SceneTimeRangeService.GetFrameDuration(viewModel.Scene);
                    MediaTime modelStart = viewModel.BorderMargin.Value.Left.PixelToTime(scale).RoundToFrame(rate, MidpointRounding.AwayFromZero);
                    // A start within a tick of a frame, as one written by hand can be, counts from that frame.
                    MediaTime deltaStart = modelStart - MediaTimeJsonConverter.SnapToFrame(viewModel.Model.Start.Value, rate);
                    int modelIndex = viewModel.Timeline.ToLayerNumber(viewModel.Margin.Value);
                    int deltaIndex = modelIndex - viewModel.Model.ZIndex.Value;
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
                    PrepareSlipPreview(viewModel, _slipTargets);
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

        private void PrepareSlipPreview(ElementViewModel viewModel, Element[] targets)
        {
            Scene scene = viewModel.Scene;
            DecoderRegistry? decoders = ExtensionGuard.FindService<DecoderRegistry>(viewModel.Timeline.EditorContext);
            var seen = new HashSet<Element>();
            var mediaTargets = new List<SlippableMedia.Target>();
            var previewElements = new List<ElementViewModel>();
            foreach (Element element in targets)
            {
                if (!seen.Add(element)) continue;
                if (!scene.Children.Contains(element)) continue;
                if (scene.IsElementLocked(element)) continue;

                List<SlippableMedia.Target> collected = SlippableMedia.Collect(element, decoders: decoders);
                if (collected.Count == 0) continue;

                mediaTargets.AddRange(collected);
                if (viewModel.Timeline.GetViewModelFor(element) is { } vm)
                    previewElements.Add(vm);
            }

            _slipMediaTargets = mediaTargets;
            _slipPreviewElements = [.. previewElements];
        }

        private void CommitSlipDrag(ElementView view, ElementViewModel viewModel)
        {
            Element[] targets = _slipTargets;
            ElementViewModel[] previewElements = _slipPreviewElements;
            List<SlippableMedia.Target> mediaTargets = _slipMediaTargets;
            _slipTargets = [];
            _slipPreviewElements = [];
            _slipMediaTargets = [];

            float scale = viewModel.Timeline.Options.Value.Scale;
            Rational rate = SceneTimeRangeService.GetFrameRate(viewModel.Scene);
            bool alt = _lastAlt;
            MediaTime delta = TrimDeltaCalculator.SnappedDelta(
                    _start.X.PixelToTime(scale),
                    _lastPosition.X.PixelToTime(scale),
                    t => view.SnapTime(t, scale, alt).Time)
                .RoundToFrame(rate, MidpointRounding.AwayFromZero);
            MediaTime effectiveDelta = delta != MediaTime.Zero && mediaTargets.Count > 0
                ? SlippableMedia.ClampSharedDelta(mediaTargets, delta)
                : MediaTime.Zero;
            bool applied = false;
            if (delta != MediaTime.Zero && targets.Length > 0)
            {
                applied = viewModel.Timeline.EditorContext
                    .GetRequiredService<IElementSlipService>()
                    .Slip(viewModel.Scene, targets, delta);
            }

            double? finalOffsetX = applied ? -effectiveDelta.TimeToPixel(scale) : null;
            foreach (ElementViewModel vm in previewElements)
            {
                vm.EndMediaPreview(committed: applied, finalContentOffsetX: finalOffsetX);
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

            view.ClearSnapFeedback();

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
            MediaTime deltaStart = newStart - viewModel.Model.Start.Value;
            int newIndex = viewModel.Timeline.ToLayerNumber(viewModel.Margin.Value);
            int deltaIndex = newIndex - viewModel.Model.ZIndex.Value;
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
                viewModel.Timeline.EditorContext.GetRequiredService<INotificationService>().ShowError(Strings.Duplicate_Failed, Strings.Duplicate_FallbackFailed);
                return;
            }

            switch (outcome)
            {
                case ElementMoveOutcome.DuplicateOverlapsSource:
                    ForceRestoreVisualToModel(relatedElements);
                    return;
                case ElementMoveOutcome.FellBackToMove:
                    viewModel.Timeline.EditorContext.GetRequiredService<INotificationService>().ShowWarning(Strings.Duplicate_Failed, Strings.Duplicate_FallbackToMove);
                    break;
                case ElementMoveOutcome.None:
                    // Zero net delta (sub-frame drag), but OnPointerMoved already shifted
                    // Margin/BorderMargin; snap visuals back so clips aren't left offset.
                    ForceRestoreVisualToModel(relatedElements);
                    return;
            }

            await AnimateToModelAsync(
                animations,
                ex => s_logger.LogWarning(ex, "Animation failed after duplicate/move; snapping visuals to model."));
        }

        private void SpawnGhostsForRelatedElements(
            IReadOnlyList<ElementViewModel> related,
            TimelineTabView timeline)
        {
            foreach (ElementViewModel vm in related)
            {
                float scale = vm.Timeline.Options.Value.Scale;
                double left = vm.Model.Start.Value.TimeToPixel(scale);
                double top = vm.Timeline.CalculateLayerTop(vm.Model.ZIndex.Value);
                double width = vm.Model.Length.Value.TimeToPixel(scale);

                var ghost = new Border
                {
                    Background = new ImmutableSolidColorBrush(vm.Color.Value),
                    Opacity = 0.4,
                    Width = width,
                    Height = vm.Timeline.LayerRowHeight.Value,
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
