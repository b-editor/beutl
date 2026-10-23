using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed partial class ElementResizeService
{
    public bool Roll(Scene scene, IReadOnlyList<ElementTrimPair> pairs, MediaTime delta)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(pairs);
        ThrowIfAnyNullParticipant(pairs);
        if (pairs.Count == 0) return false;

        // One shared delta moves every cut, so a single invalid pair rejects the whole
        // operation — a partial roll would desync the grouped cuts it was asked to keep together.
        if (!CanRollPairs(scene, pairs)) return false;

        TrimConstraints constraints = CreateTrimConstraints(scene, pairs);
        MediaTime clamped = constraints.Clamp(delta);
        if (clamped == MediaTime.Zero || !constraints.TryGetOffsetChanges(clamped, out var changes)) return false;

        for (int i = 0; i < pairs.Count; i++)
        {
            (Element front, Element back) = pairs[i];
            // Bypass Scene.MoveChild's overlap handling: Roll intentionally keeps the
            // two clips exactly adjacent (front.End == back.Start), which MoveCommand
            // treats as overlap and refuses. Direct property setters still record.
            front.Length.Value += clamped;
            back.Start.Value += clamped;
            back.Length.Value -= clamped;
        }
        SlippableMedia.ApplyOffsetChanges(changes);

        _historyManager.Commit(CommandNames.RollElements);
        return true;
    }

    public bool Slide(Scene scene, IReadOnlyList<ElementSlideLane> lanes, MediaTime delta)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(lanes);
        ThrowIfInvalidLanes(lanes);

        if (lanes.Count == 0) return false;

        // One shared delta moves every lane, so a single invalid lane rejects the whole
        // operation — a partial slide would desync the grouped block it was asked to keep together.
        if (!CanSlideLanes(scene, lanes)) return false;

        TrimConstraints constraints = CreateTrimConstraints(scene,
            lanes.Select(l => new ElementTrimPair(l.Front, l.Back)).ToArray(),
            lanes.SelectMany(l => l.Middles));
        MediaTime clamped = constraints.Clamp(delta);
        if (clamped == MediaTime.Zero || !constraints.TryGetOffsetChanges(clamped, out var changes)) return false;

        for (int i = 0; i < lanes.Count; i++)
        {
            (Element front, IReadOnlyList<Element> middles, Element back) = lanes[i];
            // Invariant per lane: front.Length + Σ middles.Length + back.Length is unchanged.
            front.Length.Value += clamped;
            foreach (Element middle in middles)
            {
                middle.Start.Value += clamped;
            }

            back.Start.Value += clamped;
            back.Length.Value -= clamped;
        }
        SlippableMedia.ApplyOffsetChanges(changes);

        _historyManager.Commit(CommandNames.SlideElements);
        return true;
    }

    internal sealed class TrimConstraints(
        MediaTime min, MediaTime max, List<SlippableMedia.Target> fronts,
        List<SlippableMedia.Target> backs, List<SlippableMedia.Target> middles,
        HashSet<IProperty<MediaTime>> fixedOffsets, bool clampEnd)
    {
        public MediaTime Min { get; } = min;
        public MediaTime Max { get; } = max;

        public MediaTime Clamp(MediaTime requested)
        {
            if (fronts.Concat(middles).Any(t => !t.IsSupported)
                || backs.Any(t => !t.CanWriteOffsets)) return MediaTime.Zero;
            MediaTime delta = ElementResizeService.Clamp(requested, Min, Max);
            // Conflicting shared offsets cannot be reconciled by shrinking the
            // edit to a rounding-sized movement that happens to have equal ticks.
            if (!TryGetOffsetChanges(delta, out _)) return MediaTime.Zero;
            // Animated local clocks restart at the new in-point. Their allowed
            // deltas need not form an interval, so validate the actual requested
            // post-trim window, not just the two endpoints of the geometry bounds.
            bool Fits(MediaTime d) =>
                (!clampEnd || fronts.All(t => t.Fits(t.Length + d, allowRecovery: true)))
                && (!clampEnd || middles.All(t => t.Fits(t.Length, d, allowRecovery: true)))
                && backs.All(t => t.CanTrim(d, clampEnd))
                && TryGetOffsetChanges(d, out _);
            MediaTime result = SlippableMedia.ClampDelta(delta, Fits);
            foreach (SlippableMedia.Target target in fronts.Where(t => t.HasVariableDuration))
            {
                var limits = new SlippableMedia.ResizeConstraints(MediaTime.Zero, target.Length, [target]);
                result = limits.SearchDurationPhases(target.Length + result, target.Length + delta,
                    MediaTime.Zero, length => Fits(length - target.Length)) - target.Length;
            }
            foreach (SlippableMedia.Target target in backs.Where(t => t.HasVariableDuration))
            {
                var limits = new SlippableMedia.ResizeConstraints(MediaTime.Zero, target.Length, [target]);
                result = target.Length - limits.SearchDurationPhases(target.Length - result, target.Length - delta,
                    MediaTime.Zero, length => Fits(target.Length - length));
            }
            return result;
        }

        public bool TryGetOffsetChanges(MediaTime delta, out Dictionary<IProperty<MediaTime>, MediaTime> changes)
            => SlippableMedia.TryGetOffsetChanges(backs, delta, trim: true, out changes)
                && changes.All(change => change.Value == MediaTime.Zero || !fixedOffsets.Contains(change.Key));
    }

    // The view keeps this snapshot for the gesture, including its integral caches.
    // Commit creates a fresh snapshot after validating membership and locks.
    internal static TrimConstraints CreateTrimConstraints(Scene scene, IReadOnlyList<ElementTrimPair> pairs,
        IEnumerable<Element>? fixedElements = null)
    {
        var fronts = new List<SlippableMedia.Target>();
        var backs = new List<SlippableMedia.Target>();
        var middles = new List<SlippableMedia.Target>();
        var fixedOffsets = new HashSet<IProperty<MediaTime>>();
        var movedElements = fixedElements?.ToHashSet() ?? [];
        var timingRoles = new Dictionary<Element, MediaTimeMapping.TrimRole>();
        foreach ((Element front, Element back) in pairs)
        {
            timingRoles[front] = MediaTimeMapping.TrimRole.Front;
            timingRoles[back] = MediaTimeMapping.TrimRole.Back;
        }
        foreach (Element middle in movedElements) timingRoles[middle] = MediaTimeMapping.TrimRole.Middle;
        var timingPeers = timingRoles.Keys.ToHashSet();
        MediaTime min = MediaTime.Zero;
        MediaTime max = MediaTime.Zero;
        for (int i = 0; i < pairs.Count; i++)
        {
            (Element front, Element back) = pairs[i];
            List<SlippableMedia.Target> frontTargets = SlippableMedia.Collect(front, timingPeers, timingRoles: timingRoles);
            List<SlippableMedia.Target> backTargets = SlippableMedia.Collect(back, timingPeers, timingRoles: timingRoles);
            fronts.AddRange(frontTargets);
            backs.AddRange(backTargets);
            fixedOffsets.UnionWith(frontTargets.Select(t => t.Offset));
            (MediaTime pairMin, MediaTime pairMax) = ComputeTrimDeltaBounds(scene, front, back, frontTargets, backTargets);
            if (i == 0 || pairMin > min) min = pairMin;
            if (i == 0 || pairMax < max) max = pairMax;
        }
        if (movedElements.Count > 0)
        {
            foreach (Element element in movedElements)
                middles.AddRange(SlippableMedia.Collect(element, timingPeers, timingRoles: timingRoles));
            fixedOffsets.UnionWith(middles.Select(t => t.Offset));
        }
        return new TrimConstraints(min, max, fronts, backs, middles, fixedOffsets,
            GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength.Value);
    }

    private static void ThrowIfAnyNullParticipant(IReadOnlyList<ElementTrimPair> pairs)
    {
        foreach ((Element front, Element back) in pairs)
        {
            if (front is null || back is null)
                throw new ArgumentNullException(nameof(pairs), "pairs must not contain null participants.");
        }
    }

    private static bool CanRollPairs(Scene scene, IReadOnlyList<ElementTrimPair> pairs)
    {
        var used = new HashSet<Element>();
        foreach ((Element front, Element back) in pairs)
        {
            if (front == back) return false;
            // Coincidental time adjacency across layers is not an editable cut, and elements
            // outside the supplied scene must not be mutated through this public seam — the
            // UI guarantees both, direct service callers may not.
            if (front.ZIndex.Value != back.ZIndex.Value) return false;
            if (!scene.Children.Contains(front) || !scene.Children.Contains(back)) return false;
            if (front.Range.End != back.Start.Value) return false;
            // Roll writes both clips, so a locked side blocks the whole op rather than being filtered.
            if (scene.IsElementLocked(front) || scene.IsElementLocked(back)) return false;
            // An element in two pairs would take two geometry writes and break the invariant.
            if (!used.Add(front) || !used.Add(back)) return false;
        }

        return true;
    }

    private static void ThrowIfInvalidLanes(IReadOnlyList<ElementSlideLane> lanes)
    {
        foreach ((Element front, IReadOnlyList<Element> middles, Element back) in lanes)
        {
            if (front is null || back is null || middles is null)
                throw new ArgumentNullException(nameof(lanes), "lanes must not contain null participants.");
            if (middles.Count == 0)
                throw new ArgumentException("Every lane needs at least one middle element.", nameof(lanes));
            foreach (Element middle in middles)
            {
                if (middle is null)
                    throw new ArgumentNullException(nameof(lanes), "lanes must not contain null participants.");
            }
        }
    }

    private static bool CanSlideLanes(Scene scene, IReadOnlyList<ElementSlideLane> lanes)
    {
        var used = new HashSet<Element>();
        foreach ((Element front, IReadOnlyList<Element> middles, Element back) in lanes)
        {
            if (!used.Add(front) || !used.Add(back)) return false;
            if (front.ZIndex.Value != back.ZIndex.Value) return false;
            if (!scene.Children.Contains(front) || !scene.Children.Contains(back)) return false;
            if (scene.IsElementLocked(front) || scene.IsElementLocked(back)) return false;

            MediaTime expectedStart = front.Range.End;
            foreach (Element middle in middles)
            {
                if (!used.Add(middle)) return false;
                if (middle.ZIndex.Value != front.ZIndex.Value) return false;
                if (!scene.Children.Contains(middle)) return false;
                if (scene.IsElementLocked(middle)) return false;
                if (middle.Start.Value != expectedStart) return false;
                expectedStart = middle.Range.End;
            }

            if (back.Start.Value != expectedStart) return false;
        }

        return true;
    }

    // Shared Roll/Slide delta window. Min (≤ 0) is bounded by the shrinking front keeping one
    // frame and — always, regardless of the clamp preference — by the back in-point staying at
    // or above zero (a negative source offset is an invalid frame request, not an
    // original-length extension). Max (≥ 0) is bounded by the shrinking back keeping one frame
    // and, when ClampResizeToOriginalLength is on, by the front out-point staying within its
    // source (matching normal edge resize, which may run past the media with the preference off).
    private static (MediaTime Min, MediaTime Max) ComputeTrimDeltaBounds(
        Scene scene, Element front, Element back,
        IReadOnlyList<SlippableMedia.Target> frontTargets,
        IReadOnlyList<SlippableMedia.Target> backTargets)
    {
        MediaTime minDuration = SceneTimeRangeService.GetFrameDuration(scene);
        // Clip ends within a tick of a frame, as a template's can be, count from that frame.
        MediaTime frontLength = SceneTimeRangeService.SnapToFrame(scene, front.Range.End)
                                - SceneTimeRangeService.SnapToFrame(scene, front.Start.Value);
        MediaTime backLength = SceneTimeRangeService.SnapToFrame(scene, back.Range.End)
                               - SceneTimeRangeService.SnapToFrame(scene, back.Start.Value);

        if (frontLength < minDuration || backLength < minDuration)
            return (MediaTime.Zero, MediaTime.Zero);

        MediaTime min = minDuration - frontLength;
        MediaTime max = backLength - minDuration;

        if (GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength.Value
            && frontTargets.All(t => !t.HasVariableDuration))
        {
            MediaTime outRoom = SlippableMedia.OutPointRoom(frontTargets, front.Length.Value, max);
            if (outRoom < max) max = outRoom;
        }

        bool clampEnd = GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength.Value;
        if (backTargets.All(t => !t.HasVariableDuration))
        {
            min = SlippableMedia.ClampInPointDelta(backTargets, min, clampEnd);
            max = SlippableMedia.ClampInPointDelta(backTargets, max, clampEnd);
        }

        // Enforce the documented Min ≤ 0 ≤ Max contract structurally instead of relying on
        // every media OffsetPosition being non-negative (an invariant owned by other services).
        if (min > MediaTime.Zero) min = MediaTime.Zero;
        if (max < MediaTime.Zero) max = MediaTime.Zero;

        return (min, max);
    }

    private static MediaTime Clamp(MediaTime value, MediaTime min, MediaTime max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
