using Beutl.Configuration;
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

        bool autoAdjustSceneDuration = ripple && GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;
        var oldBounds = ripple ? new Dictionary<Element, (int ZIndex, TimeSpan Start, TimeSpan End)>(requests.Count) : null;
        var clamped = ripple ? new Dictionary<Element, (TimeSpan Start, TimeSpan Length)>(requests.Count) : null;
        if (ripple)
        {
            var resizedSet = new HashSet<Element>(requests.Select(r => r.Element));
            foreach (ElementResizeRequest req in requests)
            {
                ValidateRippleRequest(req);
                // Clamp computed against pre-mutation state so the write loop applies a floor-safe start.
                (TimeSpan start, TimeSpan length) = ClampRippleStart(scene, req, resizedSet);
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

    private static void ValidateRippleRequest(ElementResizeRequest req)
    {
        ArgumentNullException.ThrowIfNull(req.Element);

        if (req.NewStart < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ElementResizeRequest.NewStart));
        }

        if (req.NewLength <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ElementResizeRequest.NewLength));
        }
    }

    // Limits a same-layer left-edge grow so the rigid ripple shift cannot push any upstream element
    // below zero or onto a locked clip, keeping the requested end. Clamps rather than throws: UI
    // callers run on an async-void pointer path, and frame rounding at submission can dip below the
    // preview floor.
    private static (TimeSpan Start, TimeSpan Length) ClampRippleStart(
        Scene scene, ElementResizeRequest req, IReadOnlyCollection<Element> resized)
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
        if (clampedLength <= TimeSpan.Zero)
        {
            // Requested end sits before the clamp point: keeping upstream in bounds and the end while
            // staying positive is impossible. A left-edge resize keeps the end, so the UI never reaches this.
            throw new ArgumentOutOfRangeException(
                nameof(ElementResizeRequest.NewLength),
                "Ripple left-shift cannot preserve the requested end with a positive length.");
        }

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

    public (TimeSpan Min, TimeSpan Max) GetTrimDeltaBounds(Scene scene, Element front, Element back)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(front);
        ArgumentNullException.ThrowIfNull(back);

        return ComputeTrimDeltaBounds(scene, front, back,
            SlippableMedia.Collect(front), SlippableMedia.Collect(back));
    }

    public bool Roll(Scene scene, Element front, Element back, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(front);
        ArgumentNullException.ThrowIfNull(back);
        if (front == back) return false;
        // Coincidental time adjacency across layers is not an editable cut, and elements
        // outside the supplied scene must not be mutated through this public seam — the
        // UI guarantees both, direct service callers may not.
        if (front.ZIndex != back.ZIndex) return false;
        if (!scene.Children.Contains(front) || !scene.Children.Contains(back)) return false;
        if (front.Range.End != back.Start) return false;

        List<SlippableMedia.Target> frontTargets = SlippableMedia.Collect(front);
        List<SlippableMedia.Target> backTargets = SlippableMedia.Collect(back);
        (TimeSpan min, TimeSpan max) = ComputeTrimDeltaBounds(scene, front, back, frontTargets, backTargets);
        TimeSpan clamped = Clamp(delta, min, max);
        if (clamped == TimeSpan.Zero) return false;

        // Bypass Scene.MoveChild's overlap handling: Roll intentionally keeps the
        // two clips exactly adjacent (front.End == back.Start), which MoveCommand
        // treats as overlap and refuses. Direct property setters still record.
        front.Length += clamped;
        back.Start += clamped;
        back.Length -= clamped;
        // Preserve the back clip's content across the moving cut: its in-point advances
        // by the same delta so the same source frames stay under the same timeline times.
        SlippableMedia.ApplyOffsetDelta(backTargets, clamped);

        _historyManager.Commit(CommandNames.RollElements);
        return true;
    }

    public bool Slide(Scene scene, Element front, Element middle, Element back, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(front);
        ArgumentNullException.ThrowIfNull(middle);
        ArgumentNullException.ThrowIfNull(back);
        if (front == middle || middle == back || front == back) return false;
        if (front.ZIndex != middle.ZIndex || middle.ZIndex != back.ZIndex) return false;
        if (!scene.Children.Contains(front) || !scene.Children.Contains(middle) || !scene.Children.Contains(back))
            return false;
        if (front.Range.End != middle.Start) return false;
        if (middle.Range.End != back.Start) return false;

        // The middle clip's length is unaffected by Slide, so only front and back bound the delta.
        List<SlippableMedia.Target> frontTargets = SlippableMedia.Collect(front);
        List<SlippableMedia.Target> backTargets = SlippableMedia.Collect(back);
        (TimeSpan min, TimeSpan max) = ComputeTrimDeltaBounds(scene, front, back, frontTargets, backTargets);
        TimeSpan clamped = Clamp(delta, min, max);
        if (clamped == TimeSpan.Zero) return false;

        // Invariant: front.Length + middle.Length + back.Length is unchanged.
        front.Length += clamped;
        middle.Start += clamped;
        back.Start += clamped;
        back.Length -= clamped;
        // The middle clip only shifts in time (its in-point is unchanged), but the back clip is
        // trimmed at its head, so advance its media offset by the same delta to keep its content.
        SlippableMedia.ApplyOffsetDelta(backTargets, clamped);

        _historyManager.Commit(CommandNames.SlideElements);
        return true;
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
            TimeSpan outRoom = SlippableMedia.OutPointRoom(frontTargets, front.Length);
            if (outRoom < max) max = outRoom;
        }

        TimeSpan inRoom = SlippableMedia.InPointRoom(backTargets);
        if (inRoom != TimeSpan.MaxValue && -inRoom > min) min = -inRoom;

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
