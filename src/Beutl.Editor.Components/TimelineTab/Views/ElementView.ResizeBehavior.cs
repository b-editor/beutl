using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Xaml.Interactivity;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class ElementView
{
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
    private static MediaTime? GetLeftmostUpstreamStart(Scene scene, Element element)
    {
        MediaTime? leftmost = null;
        foreach (Element other in scene.Children)
        {
            if (other != element && other.ZIndex.Value == element.ZIndex.Value && other.Range.End <= element.Start.Value)
            {
                if (leftmost is not { } cur || other.Start.Value < cur)
                {
                    leftmost = other.Start.Value;
                }
            }
        }

        return leftmost;
    }

    // Restore previews when a drag is cancelled or its completion animation fails.
    private static void ForceRestoreVisualToModel(IEnumerable<ElementViewModel> targets)
    {
        foreach (ElementViewModel vm in targets)
        {
            // Restoration must not let one element's failure escape an event handler.
            try
            {
                float scale = vm.Timeline.Options.Value.Scale;
                vm.BorderMargin.Value = new Thickness(vm.Model.Start.Value.TimeToPixel(scale), 0, 0, 0);
                vm.Margin.Value = new Thickness(0, vm.Timeline.CalculateLayerTop(vm.Model.ZIndex.Value), 0, 0);
                vm.Width.Value = vm.Model.Length.Value.TimeToPixel(scale);
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "Failed to restore visual state to model for element {Id}.", vm.Model.Id.Value);
            }
        }
    }

    private sealed class _ResizeBehavior : Behavior<ElementView>
    {
        private record struct ElementResizeContext(
            ElementViewModel ViewModel,
            Element? Before,
            Element? After,
            MediaTime RecordedStartTime,
            MediaTime RecordedEndTime,
            MediaTime? LeftmostUpstreamStart,
            MediaTime? OriginalDuration,
            SlippableMedia.ResizeConstraints? MediaConstraints);

        private enum TrimDragKind { None, Roll, Slide }

        private record struct TrimSegment(
            ElementViewModel ViewModel,
            double InitialLeft,
            double InitialWidth);

        private record struct TrimDragContext(
            TrimDragKind Kind,
            TrimSegment[] Fronts,
            TrimSegment[] Middles,
            TrimSegment[] Backs,
            ElementTrimPair[] RollPairs,
            ElementSlideLane[] SlideLanes,
            double InitialPointerX,
            Func<MediaTime, MediaTime> ClampDelta);

        private bool _pressed;
        private AlignmentX _resizeType;
        private ElementResizeContext[] _resizeContexts = [];
        private TrimDragContext _trimDrag;
        private Point _lastPosition;
        private bool _lastAlt;
        private DragSession? _drag;

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
            AssociatedObject.border.AddHandler(PointerCaptureLostEvent, OnBorderPointerCaptureLost);
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            _drag?.Confirm();
            if (AssociatedObject != null)
            {
                AssociatedObject.RemoveHandler(PointerMovedEvent, OnPointerMoved);
                AssociatedObject.border.RemoveHandler(PointerPressedEvent, OnBorderPointerPressed);
                AssociatedObject.border.RemoveHandler(PointerReleasedEvent, OnBorderPointerReleased);
                AssociatedObject.border.RemoveHandler(PointerMovedEvent, OnBorderPointerMoved);
                AssociatedObject.border.RemoveHandler(PointerCaptureLostEvent, OnBorderPointerCaptureLost);
            }
        }

        // A drag in progress confirms through its DragSession; a press that never moved has nothing to keep.
        private void OnBorderPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (_pressed && _drag is null)
                CancelResize();
        }

        // Escape and Undo restore both ordinary resizing and Roll/Slide previews.
        private void CancelResize()
        {
            _drag = null;
            _pressed = false;
            TrimDragContext ctx = _trimDrag;
            _trimDrag = default;
            ElementResizeContext[] resizeContexts = _resizeContexts;
            _resizeContexts = [];
            if (ctx.Kind is not TrimDragKind.None)
            {
                RestoreTrimDragVisuals(ctx);
            }
            else
            {
                ForceRestoreVisualToModel(resizeContexts.Select(context => context.ViewModel));
            }
            if (AssociatedObject is { ViewModel: { } viewModel })
            {
                viewModel.Timeline.SnapBarPosition.Value = null;
            }
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject is not { ViewModel: { } viewModel } view) return;

            Point point = e.GetPosition(view);
            float scale = viewModel.Timeline.Options.Value.Scale;
            MediaTime pointerFrame = point.X.PixelToTime(scale);

            if (view._timeline is null || !_pressed) return;

            _drag ??= DragSession.Begin(
                viewModel.Timeline.EditorContext, view.border, CommandNames.MoveElement, ConfirmResize, CancelResize);
            if (_drag is null)
            {
                // Another gesture holds history.
                CancelResize();
                return;
            }

            bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            _lastPosition = point;
            _lastAlt = alt;
            Rational rate = SceneTimeRangeService.GetFrameRate(viewModel.Scene);

            if (_trimDrag.Kind is not TrimDragKind.None)
            {
                PreviewTrimDrag(view, pointerFrame, scale, alt, rate);
                e.Handled = true;
                return;
            }

            pointerFrame = view.RoundStartTime(pointerFrame, scale, alt);
            point = point.WithX(pointerFrame.TimeToPixel(scale));
            double minWidth = SceneTimeRangeService.GetFrameDuration(viewModel.Scene).TimeToPixel(scale);

            if (view.Cursor != Cursors.Arrow && view.Cursor is { })
            {
                foreach (ElementResizeContext ctx in _resizeContexts)
                {
                    if (_resizeType == AlignmentX.Right)
                    {
                        PreviewRightEdge(ctx, point.X, scale, minWidth, viewModel.Timeline.IsRippleEnabled.Value);
                    }
                    else if (_resizeType == AlignmentX.Left && pointerFrame >= MediaTime.Zero)
                    {
                        PreviewLeftEdge(ctx, point.X, scale, minWidth, viewModel.Timeline.IsRippleEnabled.Value);
                    }
                }
                AlignSharedResizeEdges(scale);

                e.Handled = true;
            }
        }

        // Mirror the release commit exactly: snap both endpoints with the same
        // function, round to the frame rate, clamp to the bounds captured at drag
        // start. Snap the press point first so the snap guide (a RoundStartTime
        // side effect) ends up reflecting the pointer, not the press point.
        private void PreviewTrimDrag(ElementView view, MediaTime pointerFrame, float scale, bool alt, Rational rate)
        {
            MediaTime pressTime = view.RoundStartTime(
                _trimDrag.InitialPointerX.PixelToTime(scale), scale, alt);
            pointerFrame = view.RoundStartTime(pointerFrame, scale, alt);
            MediaTime previewDelta = _trimDrag.ClampDelta((pointerFrame - pressTime).RoundToFrame(rate, MidpointRounding.AwayFromZero));
            ApplyTrimDragPreview(previewDelta.TimeToPixel(scale));
        }

        private static void PreviewRightEdge(ElementResizeContext ctx, double pointerX, float scale, double minWidth, bool ripple)
        {
            double left = ctx.ViewModel.BorderMargin.Value.Left;
            double x = CalculateRightResizeX(
                pointerX,
                ctx.After?.Start.Value.TimeToPixel(scale),
                left,
                ctx.OriginalDuration?.TimeToPixel(scale),
                ripple);

            double width = Math.Max(x - left, minWidth);
            if (ctx.MediaConstraints is { } constraints)
                width = Math.Max(constraints.ClampLength(width.PixelToTime(scale)).TimeToPixel(scale), minWidth);
            ctx.ViewModel.Width.Value = width;
        }

        private static void PreviewLeftEdge(ElementResizeContext ctx, double pointerX, float scale, double minWidth, bool ripple)
        {
            double? rippleFloorX = ctx.LeftmostUpstreamStart is { } upstreamStart
                ? (ctx.RecordedStartTime - upstreamStart).TimeToPixel(scale)
                : null;
            double x = CalculateLeftResizeX(
                pointerX,
                ctx.Before?.Range.End.TimeToPixel(scale),
                rippleFloorX,
                ripple);
            if (ctx.MediaConstraints is { } constraints)
            {
                x = constraints.ClampStart(x.PixelToTime(scale)).TimeToPixel(scale);
            }

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

        private void ApplyTrimDragPreview(double deltaPixels)
        {
            TrimDragContext ctx = _trimDrag;

            foreach (TrimSegment front in ctx.Fronts)
            {
                front.ViewModel.Width.Value = front.InitialWidth + deltaPixels;
            }

            foreach (TrimSegment middle in ctx.Middles)
            {
                middle.ViewModel.BorderMargin.Value = new Thickness(middle.InitialLeft + deltaPixels, 0, 0, 0);
            }

            foreach (TrimSegment back in ctx.Backs)
            {
                back.ViewModel.BorderMargin.Value = new Thickness(back.InitialLeft + deltaPixels, 0, 0, 0);
                back.ViewModel.Width.Value = back.InitialWidth - deltaPixels;
            }
        }

        private void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is not { _timeline: not null, ViewModel: { } viewModel } view) return;

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
                if (TryStartSlideDrag(viewModel, e.GetPosition(view)))
                {
                    _pressed = true;
                    // PointerCaptureLost routes Direct, so move the capture to the border.
                    e.Pointer.Capture(view.border);
                }

                e.Handled = true;
                return;
            }

            if (leftButton && view.Cursor != Cursors.Arrow && view.Cursor is not null)
            {
                // In Slip mode an edge press is a slip, handled by _MoveBehavior; leave the
                // event unconsumed so a plain edge resize does not run in its place.
                if (viewModel.Timeline.IsSlipMode.Value)
                {
                    return;
                }

                Point timelinePosition = e.GetPosition(view);
                if (viewModel.Timeline.IsRollMode.Value)
                {
                    if (TryStartRollDrag(viewModel, timelinePosition))
                    {
                        _pressed = true;
                        e.Pointer.Capture(view.border);
                    }

                    e.Handled = true;
                    return;
                }

                BeginEdgeResize(viewModel);

                _pressed = true;
                e.Handled = true;
            }
        }

        private void BeginEdgeResize(ElementViewModel viewModel)
        {
            IReadOnlyList<ElementViewModel> relatedElements = viewModel.GetGroupOrSelectedElements()
                .Where(el => el.IsEditable.Value)
                .ToArray();

            IEnumerable<ElementViewModel> filteredElements;
            if (_resizeType == AlignmentX.Right)
            {
                MediaTime targetEndTime = viewModel.Model.Range.End;
                filteredElements = relatedElements.Where(elem => elem.Model.Range.End == targetEndTime);
            }
            else if (_resizeType == AlignmentX.Left)
            {
                MediaTime targetStartTime = viewModel.Model.Start.Value;
                filteredElements = relatedElements.Where(elem => elem.Model.Start.Value == targetStartTime);
            }
            else
            {
                filteredElements = [viewModel];
            }

            bool clampToOriginal = GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength.Value;
            ElementViewModel[] participants = filteredElements.ToArray();
            var timingPeers = participants.Select(elem => elem.Model).ToHashSet();

            _resizeContexts = participants.Select(elem =>
            {
                var constraints = clampToOriginal ? SlippableMedia.CreateResizeConstraints(elem.Model, timingPeers) : null;
                MediaTime? originalDuration = _resizeType == AlignmentX.Right && constraints?.HasMonotonicDuration == true
                    ? constraints.GetMaximumDuration()
                    : null;

                return new ElementResizeContext(
                    ViewModel: elem,
                    Before: elem.Model.GetBefore(elem.Model.ZIndex.Value, elem.Model.Start.Value),
                    After: elem.Model.GetAfter(elem.Model.ZIndex.Value, elem.Model.Range.End),
                    RecordedStartTime: elem.Model.Start.Value,
                    RecordedEndTime: elem.Model.Range.End,
                    LeftmostUpstreamStart: GetLeftmostUpstreamStart(viewModel.Scene, elem.Model),
                    OriginalDuration: originalDuration,
                    MediaConstraints: constraints);
            }).ToArray();
        }

        private void AlignSharedResizeEdges(float scale)
        {
            var limits = _resizeContexts.Select(ctx => ctx.MediaConstraints)
                .OfType<SlippableMedia.ResizeConstraints>().ToArray();
            if (!limits.Any(limit => limit.HasSharedClock)) return;
            bool leftEdge = _resizeType == AlignmentX.Left;
            MediaTime delta = _resizeContexts.Select(ctx => leftEdge
                    ? ctx.ViewModel.BorderMargin.Value.Left.PixelToTime(scale) - ctx.RecordedStartTime
                    : ctx.ViewModel.Width.Value.PixelToTime(scale) - (ctx.RecordedEndTime - ctx.RecordedStartTime))
                .MinBy(value => Math.Abs(value.Flicks));
            delta = SlippableMedia.ClampSharedResizeDelta(limits, delta, leftEdge);
            foreach (ElementResizeContext ctx in _resizeContexts)
            {
                MediaTime start = ctx.RecordedStartTime + (leftEdge ? delta : MediaTime.Zero);
                MediaTime end = ctx.RecordedEndTime + (leftEdge ? MediaTime.Zero : delta);
                ctx.ViewModel.BorderMargin.Value = new Thickness(start.TimeToPixel(scale), 0, 0, 0);
                ctx.ViewModel.Width.Value = (end - start).TimeToPixel(scale);
            }
        }

        private bool TryStartRollDrag(ElementViewModel viewModel, Point position)
        {
            if (_resizeType is not (AlignmentX.Left or AlignmentX.Right)) return false;

            Element model = viewModel.Model;
            MediaTime boundary = _resizeType == AlignmentX.Right ? model.Range.End : model.Start.Value;

            IReadOnlyList<ElementTrimPair>? pairs = TrimGroupCollector.CollectRollPairs(
                viewModel.Scene, CollectTrimMembers(viewModel), boundary);
            if (pairs is null) return false;
            // The pressed clip's own cut must participate; without it the drag would roll
            // only other group members' cuts, detached from the pointer.
            if (!pairs.Any(p => p.Front == model || p.Back == model)) return false;

            var fronts = new TrimSegment[pairs.Count];
            var backs = new TrimSegment[pairs.Count];
            for (int i = 0; i < pairs.Count; i++)
            {
                ElementViewModel? frontVm = viewModel.Timeline.GetViewModelFor(pairs[i].Front);
                ElementViewModel? backVm = viewModel.Timeline.GetViewModelFor(pairs[i].Back);
                if (frontVm is null || backVm is null) return false;
                fronts[i] = new TrimSegment(frontVm, frontVm.BorderMargin.Value.Left, frontVm.Width.Value);
                backs[i] = new TrimSegment(backVm, backVm.BorderMargin.Value.Left, backVm.Width.Value);
            }

            var constraints = ElementResizeService.CreateTrimConstraints(viewModel.Scene, pairs);

            _trimDrag = new TrimDragContext(
                Kind: TrimDragKind.Roll,
                Fronts: fronts,
                Middles: [],
                Backs: backs,
                RollPairs: pairs.ToArray(),
                SlideLanes: [],
                InitialPointerX: position.X,
                ClampDelta: constraints.Clamp);
            return true;
        }

        private bool TryStartSlideDrag(ElementViewModel viewModel, Point position)
        {
            IReadOnlyList<ElementSlideLane>? lanes = TrimGroupCollector.CollectSlideLanes(
                viewModel.Scene, CollectTrimMembers(viewModel));
            if (lanes is null) return false;

            var fronts = new TrimSegment[lanes.Count];
            var backs = new TrimSegment[lanes.Count];
            var middles = new List<TrimSegment>();
            for (int i = 0; i < lanes.Count; i++)
            {
                ElementViewModel? frontVm = viewModel.Timeline.GetViewModelFor(lanes[i].Front);
                ElementViewModel? backVm = viewModel.Timeline.GetViewModelFor(lanes[i].Back);
                if (frontVm is null || backVm is null) return false;
                fronts[i] = new TrimSegment(frontVm, frontVm.BorderMargin.Value.Left, frontVm.Width.Value);
                backs[i] = new TrimSegment(backVm, backVm.BorderMargin.Value.Left, backVm.Width.Value);

                foreach (Element middle in lanes[i].Middles)
                {
                    ElementViewModel? middleVm = viewModel.Timeline.GetViewModelFor(middle);
                    if (middleVm is null) return false;
                    middles.Add(new TrimSegment(middleVm, middleVm.BorderMargin.Value.Left, middleVm.Width.Value));
                }
            }

            var constraints = ElementResizeService.CreateTrimConstraints(viewModel.Scene,
                lanes.Select(l => new ElementTrimPair(l.Front, l.Back)).ToArray(),
                lanes.SelectMany(l => l.Middles));

            _trimDrag = new TrimDragContext(
                Kind: TrimDragKind.Slide,
                Fronts: fronts,
                Middles: middles.ToArray(),
                Backs: backs,
                RollPairs: [],
                SlideLanes: lanes.ToArray(),
                InitialPointerX: position.X,
                ClampDelta: constraints.Clamp);
            return true;
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
                ConfirmResize();
        }

        // Release, capture loss, playback start and detach all keep the resize where it reached.
        private async void ConfirmResize()
        {
            _drag = null;
            _pressed = false;

            if (AssociatedObject is { ViewModel: { } viewModel } view)
            {
                viewModel.Timeline.SnapBarPosition.Value = null;

                if (_trimDrag.Kind is not TrimDragKind.None)
                {
                    CommitTrimDrag(view, viewModel);
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
                    CommitGroupResize(viewModel, ripple, leftEdge);
                }
            }

            _resizeContexts = [];
        }

        private void CommitTrimDrag(ElementView view, ElementViewModel viewModel)
        {
            TrimDragContext ctx = _trimDrag;
            _trimDrag = default;

            float scale = viewModel.Timeline.Options.Value.Scale;
            Rational rate = SceneTimeRangeService.GetFrameRate(viewModel.Scene);
            bool alt = _lastAlt;
            MediaTime delta = TrimDeltaCalculator.SnappedDelta(
                    ctx.InitialPointerX.PixelToTime(scale),
                    _lastPosition.X.PixelToTime(scale),
                    t => view.RoundStartTime(t, scale, alt))
                .RoundToFrame(rate, MidpointRounding.AwayFromZero);
            // RoundStartTime re-sets the snap guide line as a side effect; clear it.
            viewModel.Timeline.SnapBarPosition.Value = null;

            RestoreTrimDragVisuals(ctx);

            if (delta != MediaTime.Zero)
            {
                IElementResizeService resizeService = viewModel.Timeline.EditorContext
                    .GetRequiredService<IElementResizeService>();
                if (ctx.Kind == TrimDragKind.Roll)
                {
                    resizeService.Roll(viewModel.Scene, ctx.RollPairs, delta);
                }
                else
                {
                    resizeService.Slide(viewModel.Scene, ctx.SlideLanes, delta);
                }
            }
        }

        private void CommitGroupResize(ElementViewModel viewModel, bool ripple, bool leftEdge)
        {
            var animations = _resizeContexts
                .Select(x => (ViewModel: x.ViewModel, Context: x.ViewModel.PrepareAnimation()))
                .ToArray();

            float scale = viewModel.Timeline.Options.Value.Scale;
            Rational rate = SceneTimeRangeService.GetFrameRate(viewModel.Scene);

            MediaTime RoundEdge(ElementResizeContext ctx)
            {
                double edge = ctx.ViewModel.BorderMargin.Value.Left + (leftEdge ? 0 : ctx.ViewModel.Width.Value);
                return edge.PixelToTime(scale).RoundToFrame(rate, MidpointRounding.AwayFromZero);
            }
            MediaTime? sharedEdge = _resizeContexts.Any(ctx => ctx.MediaConstraints?.HasSharedClock == true)
                ? RoundEdge(_resizeContexts[0]) : null;
            var requests = new ElementResizeRequest[_resizeContexts.Length];
            for (int i = 0; i < _resizeContexts.Length; i++)
            {
                ElementResizeContext ctx = _resizeContexts[i];
                // Round the moving edge once and retain the opposite edge.
                // Rounding each start/length separately can split a shared
                // edge when the clips have different sub-frame starts.
                MediaTime edge = sharedEdge ?? RoundEdge(ctx);
                (MediaTime newStart, MediaTime newLength) = leftEdge
                    ? (edge, ctx.RecordedEndTime - edge)
                    : (ctx.RecordedStartTime, edge - ctx.RecordedStartTime);
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

        private static void RestoreTrimDragVisuals(TrimDragContext ctx)
        {
            foreach (TrimSegment front in ctx.Fronts)
            {
                front.ViewModel.Width.Value = front.InitialWidth;
            }

            foreach (TrimSegment middle in ctx.Middles)
            {
                middle.ViewModel.BorderMargin.Value = new Thickness(middle.InitialLeft, 0, 0, 0);
                middle.ViewModel.Width.Value = middle.InitialWidth;
            }

            foreach (TrimSegment back in ctx.Backs)
            {
                back.ViewModel.BorderMargin.Value = new Thickness(back.InitialLeft, 0, 0, 0);
                back.ViewModel.Width.Value = back.InitialWidth;
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
                    double minWidth = SceneTimeRangeService.GetFrameDuration(viewModel.Scene).TimeToPixel(scale);

                    Point point = e.GetPosition(border);
                    double horizon = point.X;

                    double handleWidth = 10;
                    if (border.Width <= minWidth)
                    {
                        handleWidth = minWidth / 2;
                    }

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
}
