using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class ElementResizeService : IElementResizeService
{
    private readonly HistoryManager _historyManager;

    public ElementResizeService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public void Resize(Scene scene, IReadOnlyList<ElementResizeRequest> requests, bool ripple = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0) return;

        // Drop locked targets here, at the single mutation boundary the UI submits to: a clip or its
        // layer may have been locked mid-drag, after the press-time guard already staged the request.
        requests = requests.Where(r => !scene.IsElementLocked(r.Element)).ToArray();
        if (requests.Count == 0) return;

        // Sub-frame original durations and pixel rounding can submit zero length from async UI handlers.
        int rate = SceneTimeRangeService.GetFrameRate(scene);
        // Invalid persisted rates use the default; sub-tick frames still require a positive duration.
        if (rate <= 0) rate = 30;
        TimeSpan minLength = TimeSpan.FromTicks((TimeSpan.TicksPerSecond + (long)rate - 1) / rate);
        requests = NormalizeRequests(requests, ripple, minLength);
        if (GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength)
        {
            requests = requests.Select(req =>
            {
                var constraints = SlippableMedia.CreateResizeConstraints(req.Element);
                if (req.NewStart != req.Element.Start && req.NewStart + req.NewLength == req.Element.Range.End)
                {
                    TimeSpan start = constraints.ClampStart(req.NewStart);
                    return req with { NewStart = start, NewLength = req.Element.Range.End - start };
                }
                TimeSpan length = constraints.ClampLength(req.NewLength, req.NewStart);
                return req with { NewLength = length < minLength ? minLength : length };
            }).ToArray();
        }

        bool autoAdjustSceneDuration = ripple && GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;
        var oldBounds = ripple ? new Dictionary<Element, (int ZIndex, TimeSpan Start, TimeSpan End)>(requests.Count) : null;
        var clamped = ripple ? new Dictionary<Element, (TimeSpan Start, TimeSpan Length)>(requests.Count) : null;
        if (ripple)
        {
            var resizedSet = new HashSet<Element>(requests.Select(r => r.Element));
            foreach (ElementResizeRequest req in requests)
            {
                // Clamp computed against pre-mutation state so the write loop applies a floor-safe start.
                (TimeSpan start, TimeSpan length) = ClampRippleStart(scene, req, resizedSet, minLength);
                length = ClampRippleEnd(scene, req, start, length, resizedSet);
                clamped![req.Element] = (start, length);
                oldBounds![req.Element] = (req.Element.ZIndex, req.Element.Start, req.Element.Range.End);
            }
        }

        if (ripple)
        {
            // MoveChild reverts on follower overlap; ripple needs that overlap, and direct
            // writes are still CoreObjectOperationObserver-recorded for undo.
            foreach (ElementResizeRequest req in requests)
            {
                (TimeSpan start, TimeSpan length) = clamped![req.Element];
                req.Element.ZIndex = req.ZIndex;
                req.Element.Start = start;
                req.Element.Length = length;
            }
        }
        else
        {
            foreach (ElementResizeRequest req in requests)
            {
                scene.MoveChild(req.ZIndex, req.NewStart, req.NewLength, req.Element);
            }
        }

        if (ripple)
        {
            Element[] resized = requests.Select(r => r.Element).ToArray();
            foreach (ElementResizeRequest req in requests)
            {
                (int oldZ, TimeSpan oldStart, TimeSpan oldEnd) = oldBounds![req.Element];
                if (req.Element.ZIndex != oldZ) continue;

                // Both run: a pure right-edge resize has startDelta == 0, a pure left-edge
                // resize has endDelta == 0, so each ShiftX no-ops on the untouched edge.
                TimeSpan endDelta = req.Element.Range.End - oldEnd;
                RippleHelper.ShiftAfter(scene, oldZ, oldEnd, endDelta, resized);

                TimeSpan startDelta = req.Element.Start - oldStart;
                RippleHelper.ShiftBefore(scene, oldZ, oldStart, startDelta, resized);
            }

            if (autoAdjustSceneDuration)
            {
                ExtendSceneDurationToIncludeChildren(scene);
            }
        }

        _historyManager.Commit(CommandNames.MoveElement);
    }

    private static void ExtendSceneDurationToIncludeChildren(Scene scene)
    {
        TimeSpan sceneEnd = scene.Start + scene.Duration;
        foreach (Element child in scene.Children)
        {
            if (sceneEnd < child.Range.End)
            {
                sceneEnd = child.Range.End;
            }
        }

        scene.Duration = sceneEnd - scene.Start;
    }

    private static ElementResizeRequest[] NormalizeRequests(IReadOnlyList<ElementResizeRequest> requests, bool ripple, TimeSpan minLength)
    {
        var normalized = new ElementResizeRequest[requests.Count];
        for (int i = 0; i < requests.Count; i++)
        {
            ElementResizeRequest req = requests[i];
            ArgumentNullException.ThrowIfNull(req.Element);
            TimeSpan start = req.NewStart < TimeSpan.Zero ? TimeSpan.Zero : req.NewStart;
            TimeSpan length = req.NewLength;
            if (ripple && req.NewStart < TimeSpan.Zero && length > TimeSpan.Zero)
            {
                // Preserve the requested end so clamping a left-edge drag does not ripple followers.
                length += req.NewStart;
            }

            if (length < minLength)
            {
                // A changed start with the original end identifies a fixed-right-edge resize.
                TimeSpan end = req.Element.Range.End;
                if (req.NewStart >= TimeSpan.Zero && req.NewStart != req.Element.Start
                    && req.NewLength == end - req.NewStart)
                {
                    start = end > minLength ? end - minLength : TimeSpan.Zero;
                }

                length = minLength;
            }
            normalized[i] = new ElementResizeRequest(req.Element, start, length, req.ZIndex);
        }

        return normalized;
    }

    // Limits a same-layer left-edge grow so the rigid ripple shift cannot push any upstream element
    // below zero or onto a locked clip, keeping the requested end. Clamps rather than throws: UI
    // callers run on an async-void pointer path, and frame rounding at submission can dip below the
    // preview floor.
    private static (TimeSpan Start, TimeSpan Length) ClampRippleStart(
        Scene scene, ElementResizeRequest req, IReadOnlyCollection<Element> resized, TimeSpan minLength)
    {
        if (req.ZIndex != req.Element.ZIndex) return (req.NewStart, req.NewLength);

        TimeSpan startDelta = req.NewStart - req.Element.Start;
        if (startDelta >= TimeSpan.Zero) return (req.NewStart, req.NewLength);

        // Collect the layer's locked-clip ends once and sort them; a locked clip is an immovable
        // barrier the upstream shift cannot cross, so each clip's floor is one binary search.
        var lockedEnds = new List<TimeSpan>();
        foreach (Element e in scene.Children)
        {
            if (e.ZIndex == req.Element.ZIndex && e.IsLocked) lockedEnds.Add(e.Range.End);
        }

        lockedEnds.Sort();

        // Every upstream clip shifts left by the same delta, so the grow is bounded by the tightest
        // room: each clip can move left only to the timeline start or the end of the nearest locked
        // clip in front of it (locked clips are immovable and must not be overlapped). Seed the bound
        // with the resized clip's own room so its left edge is clamped even with no free upstream clip.
        TimeSpan maxGrow = req.Element.Start - NearestLockedEndAtOrBefore(lockedEnds, req.Element.Start);
        foreach (Element e in scene.Children)
        {
            if (e.ZIndex != req.Element.ZIndex || resized.Contains(e) || e.IsLocked
                || e.Range.End > req.Element.Start)
            {
                continue;
            }

            TimeSpan room = e.Start - NearestLockedEndAtOrBefore(lockedEnds, e.Start);
            if (room < maxGrow) maxGrow = room;
        }

        if (startDelta >= -maxGrow)
        {
            return (req.NewStart, req.NewLength);
        }

        TimeSpan clampedStart = req.Element.Start - maxGrow;
        TimeSpan clampedLength = req.NewStart + req.NewLength - clampedStart;
        // If the requested end is too close to or before the barrier, keep a positive minimum.
        if (clampedLength < minLength) clampedLength = minLength;

        return (clampedStart, clampedLength);
    }

    // Largest end in the sorted list that is at or before start, or zero (the timeline floor) when
    // none qualifies.
    private static TimeSpan NearestLockedEndAtOrBefore(List<TimeSpan> sortedEnds, TimeSpan start)
    {
        int lo = 0, hi = sortedEnds.Count - 1;
        TimeSpan floor = TimeSpan.Zero;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            if (sortedEnds[mid] <= start)
            {
                floor = sortedEnds[mid];
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return floor;
    }

    // Limits a same-layer right-edge grow so the ripple shift cannot push any follower onto a
    // locked follower, which stays anchored. The rightmost non-locked follower before the lock
    // hits it first, so the growing run may only advance until that clip touches the lock.
    private static TimeSpan ClampRippleEnd(
        Scene scene, ElementResizeRequest req, TimeSpan start, TimeSpan length, IReadOnlyCollection<Element> resized)
    {
        if (req.ZIndex != req.Element.ZIndex) return length;

        TimeSpan oldEnd = req.Element.Range.End;
        TimeSpan newEnd = start + length;
        if (newEnd <= oldEnd) return length;

        TimeSpan? nearestLockedStart = null;
        foreach (Element e in scene.Children)
        {
            if (e.ZIndex == req.Element.ZIndex && e.IsLocked && e.Start >= oldEnd)
            {
                if (nearestLockedStart is not { } cur || e.Start < cur) nearestLockedStart = e.Start;
            }
        }

        if (nearestLockedStart is not { } lockStart) return length;

        TimeSpan blockingEnd = oldEnd;
        foreach (Element e in scene.Children)
        {
            if (e.ZIndex == req.Element.ZIndex && !e.IsLocked && !resized.Contains(e)
                && e.Start >= oldEnd && e.Start < lockStart && e.Range.End > blockingEnd)
            {
                blockingEnd = e.Range.End;
            }
        }

        TimeSpan maxEnd = oldEnd + (lockStart - blockingEnd);
        if (newEnd <= maxEnd) return length;

        TimeSpan clampedEnd = maxEnd > oldEnd ? maxEnd : oldEnd;
        // A start past the clamp point (e.g. the element also moved right onto the lock) would make
        // the clamped length non-positive and corrupt the range; leave the length to the caller's
        // move validation instead of forcing a bad value here.
        TimeSpan clampedLength = clampedEnd - start;
        return clampedLength > TimeSpan.Zero ? clampedLength : length;
    }

    public (TimeSpan Min, TimeSpan Max) GetTrimDeltaBounds(Scene scene, IReadOnlyList<ElementTrimPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(pairs);
        ThrowIfAnyNullParticipant(pairs);

        TrimConstraints constraints = CreateTrimConstraints(scene, pairs);
        return (constraints.Min, constraints.Max);
    }

    public bool Roll(Scene scene, IReadOnlyList<ElementTrimPair> pairs, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(pairs);
        ThrowIfAnyNullParticipant(pairs);
        if (pairs.Count == 0) return false;

        // One shared delta moves every cut, so a single invalid pair rejects the whole
        // operation — a partial roll would desync the grouped cuts it was asked to keep together.
        var used = new HashSet<Element>();
        foreach ((Element front, Element back) in pairs)
        {
            if (front == back) return false;
            // Coincidental time adjacency across layers is not an editable cut, and elements
            // outside the supplied scene must not be mutated through this public seam — the
            // UI guarantees both, direct service callers may not.
            if (front.ZIndex != back.ZIndex) return false;
            if (!scene.Children.Contains(front) || !scene.Children.Contains(back)) return false;
            if (front.Range.End != back.Start) return false;
            // Roll writes both clips, so a locked side blocks the whole op rather than being filtered.
            if (scene.IsElementLocked(front) || scene.IsElementLocked(back)) return false;
            // An element in two pairs would take two geometry writes and break the invariant.
            if (!used.Add(front) || !used.Add(back)) return false;
        }

        TrimConstraints constraints = CreateTrimConstraints(scene, pairs);
        TimeSpan clamped = constraints.Clamp(delta);
        if (clamped == TimeSpan.Zero || !constraints.TryGetOffsetChanges(clamped, out var changes)) return false;

        for (int i = 0; i < pairs.Count; i++)
        {
            (Element front, Element back) = pairs[i];
            // Bypass Scene.MoveChild's overlap handling: Roll intentionally keeps the
            // two clips exactly adjacent (front.End == back.Start), which MoveCommand
            // treats as overlap and refuses. Direct property setters still record.
            front.Length += clamped;
            back.Start += clamped;
            back.Length -= clamped;
        }
        SlippableMedia.ApplyOffsetChanges(changes);

        _historyManager.Commit(CommandNames.RollElements);
        return true;
    }

    public bool Slide(Scene scene, IReadOnlyList<ElementSlideLane> lanes, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(lanes);
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

        if (lanes.Count == 0) return false;

        // One shared delta moves every lane, so a single invalid lane rejects the whole
        // operation — a partial slide would desync the grouped block it was asked to keep together.
        var used = new HashSet<Element>();
        foreach ((Element front, IReadOnlyList<Element> middles, Element back) in lanes)
        {
            if (!used.Add(front) || !used.Add(back)) return false;
            if (front.ZIndex != back.ZIndex) return false;
            if (!scene.Children.Contains(front) || !scene.Children.Contains(back)) return false;
            if (scene.IsElementLocked(front) || scene.IsElementLocked(back)) return false;

            TimeSpan expectedStart = front.Range.End;
            foreach (Element middle in middles)
            {
                if (!used.Add(middle)) return false;
                if (middle.ZIndex != front.ZIndex) return false;
                if (!scene.Children.Contains(middle)) return false;
                if (scene.IsElementLocked(middle)) return false;
                if (middle.Start != expectedStart) return false;
                expectedStart = middle.Range.End;
            }

            if (back.Start != expectedStart) return false;
        }

        TrimConstraints constraints = CreateTrimConstraints(scene,
            lanes.Select(l => new ElementTrimPair(l.Front, l.Back)).ToArray(),
            lanes.SelectMany(l => l.Middles));
        TimeSpan clamped = constraints.Clamp(delta);
        if (clamped == TimeSpan.Zero || !constraints.TryGetOffsetChanges(clamped, out var changes)) return false;

        for (int i = 0; i < lanes.Count; i++)
        {
            (Element front, IReadOnlyList<Element> middles, Element back) = lanes[i];
            // Invariant per lane: front.Length + Σ middles.Length + back.Length is unchanged.
            front.Length += clamped;
            foreach (Element middle in middles)
            {
                middle.Start += clamped;
            }

            back.Start += clamped;
            back.Length -= clamped;
        }
        SlippableMedia.ApplyOffsetChanges(changes);

        _historyManager.Commit(CommandNames.SlideElements);
        return true;
    }

    internal sealed class TrimConstraints(
        TimeSpan min, TimeSpan max, List<SlippableMedia.Target> fronts,
        List<SlippableMedia.Target> backs, List<SlippableMedia.Target> middles,
        HashSet<IProperty<TimeSpan>> fixedOffsets, bool clampEnd)
    {
        public TimeSpan Min { get; } = min;
        public TimeSpan Max { get; } = max;

        public TimeSpan Clamp(TimeSpan requested)
        {
            if (backs.Any(t => fixedOffsets.Contains(t.Offset))) return TimeSpan.Zero;
            TimeSpan delta = ElementResizeService.Clamp(requested, Min, Max);
            // Animated local clocks restart at the new in-point. Their allowed
            // deltas need not form an interval, so validate the actual requested
            // post-trim window, not just the two endpoints of the geometry bounds.
            return SlippableMedia.ClampDelta(delta, d =>
                (!clampEnd || fronts.All(t => t.Fits(t.Length + d, allowRecovery: true)))
                && (!clampEnd || middles.All(t => t.Fits(t.Length, d, allowRecovery: true)))
                && backs.All(t => t.CanTrim(d, clampEnd))
                && TryGetOffsetChanges(d, out _));
        }

        public bool TryGetOffsetChanges(TimeSpan delta, out Dictionary<IProperty<TimeSpan>, TimeSpan> changes)
            => SlippableMedia.TryGetOffsetChanges(backs, delta, trim: true, out changes);
    }

    // The view keeps this snapshot for the gesture, including its integral caches.
    // Commit creates a fresh snapshot after validating membership and locks.
    internal static TrimConstraints CreateTrimConstraints(Scene scene, IReadOnlyList<ElementTrimPair> pairs,
        IEnumerable<Element>? fixedElements = null)
    {
        var fronts = new List<SlippableMedia.Target>();
        var backs = new List<SlippableMedia.Target>();
        var middles = new List<SlippableMedia.Target>();
        var fixedOffsets = new HashSet<IProperty<TimeSpan>>();
        var timingPeers = pairs.Select(p => p.Back).ToHashSet();
        var frontPeers = pairs.Select(p => p.Front).ToHashSet();
        TimeSpan min = TimeSpan.Zero;
        TimeSpan max = TimeSpan.Zero;
        for (int i = 0; i < pairs.Count; i++)
        {
            (Element front, Element back) = pairs[i];
            List<SlippableMedia.Target> frontTargets = SlippableMedia.Collect(front, frontPeers);
            List<SlippableMedia.Target> backTargets = SlippableMedia.Collect(back, timingPeers);
            fronts.AddRange(frontTargets);
            backs.AddRange(backTargets);
            fixedOffsets.UnionWith(frontTargets.Select(t => t.Offset));
            (TimeSpan pairMin, TimeSpan pairMax) = ComputeTrimDeltaBounds(scene, front, back, frontTargets, backTargets);
            if (i == 0 || pairMin > min) min = pairMin;
            if (i == 0 || pairMax < max) max = pairMax;
        }
        if (fixedElements != null)
        {
            var movedElements = fixedElements.ToHashSet();
            foreach (Element element in movedElements)
                middles.AddRange(SlippableMedia.Collect(element, movedElements));
            fixedOffsets.UnionWith(middles.Select(t => t.Offset));
        }
        return new TrimConstraints(min, max, fronts, backs, middles, fixedOffsets,
            GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength);
    }

    private static void ThrowIfAnyNullParticipant(IReadOnlyList<ElementTrimPair> pairs)
    {
        foreach ((Element front, Element back) in pairs)
        {
            if (front is null || back is null)
                throw new ArgumentNullException(nameof(pairs), "pairs must not contain null participants.");
        }
    }

    // Shared Roll/Slide delta window. Min (≤ 0) is bounded by the shrinking front keeping one
    // frame and — always, regardless of the clamp preference — by the back in-point staying at
    // or above zero (a negative source offset is an invalid frame request, not an
    // original-length extension). Max (≥ 0) is bounded by the shrinking back keeping one frame
    // and, when ClampResizeToOriginalLength is on, by the front out-point staying within its
    // source (matching normal edge resize, which may run past the media with the preference off).
    private static (TimeSpan Min, TimeSpan Max) ComputeTrimDeltaBounds(
        Scene scene, Element front, Element back,
        IReadOnlyList<SlippableMedia.Target> frontTargets,
        IReadOnlyList<SlippableMedia.Target> backTargets)
    {
        int rate = SceneTimeRangeService.GetFrameRate(scene);
        TimeSpan minDuration = TimeSpan.FromSeconds(1d / rate);

        if (front.Length < minDuration || back.Length < minDuration)
            return (TimeSpan.Zero, TimeSpan.Zero);

        TimeSpan min = minDuration - front.Length;
        TimeSpan max = back.Length - minDuration;

        if (GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength)
        {
            TimeSpan outRoom = SlippableMedia.OutPointRoom(frontTargets, front.Length, max);
            if (outRoom < max) max = outRoom;
        }

        bool clampEnd = GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength;
        min = SlippableMedia.ClampInPointDelta(backTargets, min, clampEnd);
        max = SlippableMedia.ClampInPointDelta(backTargets, max, clampEnd);

        // Enforce the documented Min ≤ 0 ≤ Max contract structurally instead of relying on
        // every media OffsetPosition being non-negative (an invariant owned by other services);
        // an inverted window would throw in the View's per-pointer-frame ClampDelta.
        if (min > TimeSpan.Zero) min = TimeSpan.Zero;
        if (max < TimeSpan.Zero) max = TimeSpan.Zero;

        return (min, max);
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
