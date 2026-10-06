using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class TimelineTabViewModel
{
    private void CloseSelectedGap()
    {
        Element? anchor = SelectedElements
            .OrderBy(e => e.Model.Start)
            .ThenBy(e => e.Model.ZIndex)
            .Select(e => e.Model)
            .FirstOrDefault();
        if (anchor is null)
        {
            // Reachable from the flyout menu, which unlike the shortcut path has no selection gate.
            NotificationService.ShowInformation(Strings.CloseGap, Strings.NoElementSelected);
            return;
        }

        FlushPendingNudgeCommit();
        if (!EditorContext.GetRequiredService<IElementGapService>().CloseGapAfter(Scene, anchor))
        {
            NotificationService.ShowInformation(Strings.CloseGap, Strings.NoGapsToClose);
        }
    }

    // Context-menu Close Gap acts on the right-clicked position, not the selection, so the gap the
    // user pointed at is the one that closes.
    private void CloseGapAtPointer()
    {
        FlushPendingNudgeCommit();
        // Hit-test with the raw pointer time, not the frame-rounded ClickedFrame: rounding can push a
        // click in the latter half of a one-frame gap onto the next clip's start, which the half-open
        // gap range then reports as no gap.
        TimeSpan clickedTime = ClickedPosition.X.PixelToTimeSpan(Options.Value.Scale);
        if (ResolvePointerGapToClose(Scene, clickedTime, CalculateClickedLayer()) is { } gap
            && EditorContext.GetRequiredService<IElementGapService>().CloseGapAfter(Scene, gap.Anchor))
        {
            return;
        }

        NotificationService.ShowInformation(Strings.CloseGap, Strings.NoGapsToClose);
    }

    // The gap under a right-click, but only when it lies wholly inside the active scene. ClickedFrame
    // is not clamped to the scene, so a click past either edge can land in a gap that straddles the
    // boundary; closing that raw gap would shift clips by off-scene space the user cannot see.
    internal static SceneGap? ResolvePointerGapToClose(Scene scene, TimeSpan clickedTime, int layer)
    {
        TimeSpan sceneEnd = scene.Start + scene.Duration;
        return scene.FindGapAt(clickedTime, layer) is { } gap
               && gap.Range.Start >= scene.Start && gap.Range.End <= sceneEnd
            ? gap
            : null;
    }

    private void CloseAllSceneGaps()
    {
        FlushPendingNudgeCommit();
        if (EditorContext.GetRequiredService<IElementGapService>().CloseAllGaps(Scene) == 0)
        {
            NotificationService.ShowInformation(Strings.CloseAllGaps, Strings.NoGapsToClose);
        }
    }

    private void GoToGap(bool forward)
    {
        if (FindGapNavigationTarget(Scene, CurrentTime.Value, forward) is { } target)
        {
            CurrentTime.Value = target.Target;
            // The playhead move alone does not scroll while stopped, so bring the target into view like
            // the other seek commands. Scroll around the target, not the whole gap: a gap wider than the
            // viewport always intersects it and the view would skip the scroll. The 1-tick width keeps
            // the range non-empty so the view's "already visible" check reads it as the target point.
            ScrollTo.Execute((new TimeRange(target.Target, TimeSpan.FromTicks(1)), target.ZIndex));
            // A null anchor (start-clipped gap) has no element to select; clear any prior selection so
            // the selection-based Close Gap cannot act on a stale, unrelated gap after the jump.
            if (target.Anchor is { } anchor)
            {
                SelectGapAnchor(anchor);
            }
            else
            {
                ClearSelected();
                EditorContext.GetRequiredService<IEditorSelection>().SelectedObject.Value = null;
            }
        }
        else
        {
            NotificationService.ShowInformation(
                forward ? Strings.GoToNextGap : Strings.GoToPreviousGap,
                Strings.NoGapsToGoTo);
        }
    }

    // Moving to a gap selects its anchor so the selection-based Close Gap then acts on that same gap.
    // The global editor selection is updated too, matching click selection, so the inspector and other
    // tabs follow the gap anchor instead of the previously selected element.
    private void SelectGapAnchor(Element anchor)
    {
        ElementViewModel? vm = Elements.FirstOrDefault(e => ReferenceEquals(e.Model, anchor));
        if (vm is null) return;

        ClearSelected();
        SelectElement(vm);
        EditorContext.GetRequiredService<IEditorSelection>().SelectedObject.Value = anchor;
    }

    // The search is bounded to the active scene range by searchRange, so a playhead parked outside the
    // scene still navigates within it. currentTime is passed through unclamped: clamping the origin to
    // the boundary would make the >= / <= searches skip an in-range gap that touches Scene.Start or
    // Scene.Start + Duration exactly. Returns the target playhead time — the centre of the gap's visible
    // (scene-clamped) portion — together with the gap's anchor. The anchor is null unless the raw gap is
    // wholly inside the scene: a clipped gap's anchor sits off-scene, so selecting it would make Close
    // Gap collapse the raw gap and move a clip the user cannot see.
    internal static (TimeSpan Target, int ZIndex, Element? Anchor)? FindGapNavigationTarget(
        Scene scene, TimeSpan currentTime, bool forward)
    {
        TimeSpan sceneStart = scene.Start;
        TimeSpan sceneEnd = scene.Start + scene.Duration;
        var range = new TimeRange(sceneStart, scene.Duration);
        SceneGap? gap = forward
            ? scene.FindNextGap(currentTime, range)
            : scene.FindPreviousGap(currentTime, range);
        if (gap is not { } g) return null;

        TimeSpan visibleStart = g.Range.Start > sceneStart ? g.Range.Start : sceneStart;
        TimeSpan visibleEnd = g.Range.End < sceneEnd ? g.Range.End : sceneEnd;
        TimeSpan target = visibleStart + new TimeSpan((visibleEnd - visibleStart).Ticks / 2);
        bool whollyInside = g.Range.Start >= sceneStart && g.Range.End <= sceneEnd;
        return (target, g.ZIndex, whollyInside ? g.Anchor : null);
    }
}
