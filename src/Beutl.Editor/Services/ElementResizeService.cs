using Beutl.Configuration;
using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed partial class ElementResizeService : IElementResizeService
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
        MediaTime minLength = GetMinimumLength(scene);
        requests = NormalizeRequests(requests, ripple, minLength);
        ResizePeerGroup[] peerGroups = CreatePeerGroups(requests);
        var mediaConstraints = new Dictionary<Element, SlippableMedia.ResizeConstraints>();
        requests = ClampToMedia(requests, peerGroups, mediaConstraints, minLength);
        requests = ClampSharedResizeEdges(requests, peerGroups, mediaConstraints);

        if (ripple)
        {
            ResizeWithRipple(scene, requests, peerGroups, mediaConstraints, minLength);
        }
        else
        {
            foreach (ElementResizeRequest req in requests)
            {
                scene.MoveChild(req.ZIndex, req.NewStart, req.NewLength, req.Element);
            }
        }

        _historyManager.Commit(CommandNames.MoveElement);
    }

    // One frame, but never empty: at a rate of more than a frame a flick, a frame rounds to zero.
    private static MediaTime GetMinimumLength(Scene scene)
    {
        MediaTime frame = MediaTime.FromFrames(1, SceneTimeRangeService.GetFrameRate(scene));
        return frame > MediaTime.Zero ? frame : new MediaTime(1);
    }

    private static ResizePeerGroup[] CreatePeerGroups(IReadOnlyList<ElementResizeRequest> requests)
        => requests.Select(req => (Request: req, Edge: GetResizeEdge(req)))
            .Where(entry => entry.Edge.HasValue)
            .GroupBy(entry => entry.Edge!.Value)
            .Select(group => new ResizePeerGroup(group.Key.LeftEdge, group.Select(entry => entry.Request.Element).ToHashSet()))
            .ToArray();

    private static ElementResizeRequest[] ClampToMedia(IReadOnlyList<ElementResizeRequest> requests,
        ResizePeerGroup[] peerGroups, Dictionary<Element, SlippableMedia.ResizeConstraints> mediaConstraints,
        MediaTime minLength)
        => requests.Select(req =>
        {
            if (req.ClampToSource || req.NewLength != req.Element.Length
                && GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength)
            {
                var peers = peerGroups.FirstOrDefault(group => group.Elements.Contains(req.Element))?.Elements;
                var constraints = SlippableMedia.CreateResizeConstraints(req.Element, peers);
                mediaConstraints[req.Element] = constraints;
                return ClampMedia(req, constraints, minLength);
            }
            return req;
        }).ToArray();

    private static void ResizeWithRipple(Scene scene, IReadOnlyList<ElementResizeRequest> requests,
        ResizePeerGroup[] peerGroups, Dictionary<Element, SlippableMedia.ResizeConstraints> mediaConstraints,
        MediaTime minLength)
    {
        bool autoAdjustSceneDuration = GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration;
        var oldBounds = new Dictionary<Element, (int ZIndex, MediaTime Start, MediaTime End)>(requests.Count);
        var clamped = new Dictionary<Element, (MediaTime Start, MediaTime Length)>(requests.Count);
        var resizedSet = new HashSet<Element>(requests.Select(r => r.Element));
        foreach (ElementResizeRequest req in requests)
        {
            // Clamp computed against pre-mutation state so the write loop applies a floor-safe start.
            (MediaTime start, MediaTime length) = ClampRippleStart(scene, req, resizedSet, minLength);
            length = ClampRippleEnd(scene, req, start, length, resizedSet);
            if (mediaConstraints.TryGetValue(req.Element, out var constraints))
            {
                // Barrier clamping can select an unsafe intermediate loop period.
                // Recheck the final geometry, moving towards the original edge so
                // source clamping cannot cross the locked barrier again.
                ElementResizeRequest final = ClampMedia(req with { NewStart = start, NewLength = length }, constraints, minLength);
                start = final.NewStart;
                length = final.NewLength;
            }
            clamped[req.Element] = (start, length);
            oldBounds[req.Element] = (req.Element.ZIndex, req.Element.Start, req.Element.Range.End);
        }
        var finalRequests = requests.Select(req => req with
        {
            NewStart = clamped[req.Element].Start,
            NewLength = clamped[req.Element].Length
        }).ToArray();
        foreach (ElementResizeRequest req in ClampSharedResizeEdges(finalRequests, peerGroups, mediaConstraints))
            clamped[req.Element] = (req.NewStart, req.NewLength);

        // MoveChild reverts on follower overlap; ripple needs that overlap, and direct
        // writes are still CoreObjectOperationObserver-recorded for undo.
        foreach (ElementResizeRequest req in requests)
        {
            (MediaTime start, MediaTime length) = clamped[req.Element];
            req.Element.ZIndex = req.ZIndex;
            req.Element.Start = start;
            req.Element.Length = length;
        }

        Element[] resized = requests.Select(r => r.Element).ToArray();
        foreach (ElementResizeRequest req in requests)
        {
            (int oldZ, MediaTime oldStart, MediaTime oldEnd) = oldBounds[req.Element];
            if (req.Element.ZIndex != oldZ) continue;

            // Both run: a pure right-edge resize has startDelta == 0, a pure left-edge
            // resize has endDelta == 0, so each ShiftX no-ops on the untouched edge.
            MediaTime endDelta = req.Element.Range.End - oldEnd;
            RippleHelper.ShiftAfter(scene, oldZ, oldEnd, endDelta, resized);

            MediaTime startDelta = req.Element.Start - oldStart;
            RippleHelper.ShiftBefore(scene, oldZ, oldStart, startDelta, resized);
        }

        if (autoAdjustSceneDuration)
        {
            ExtendSceneDurationToIncludeChildren(scene);
        }
    }

    private sealed record ResizePeerGroup(bool LeftEdge, HashSet<Element> Elements);

    private static bool IsLeftEdgeResize(ElementResizeRequest request)
        => request.NewStart != request.Element.Start && request.NewStart + request.NewLength == request.Element.Range.End;

    private static (bool LeftEdge, MediaTime Before, MediaTime After)? GetResizeEdge(ElementResizeRequest request)
    {
        Element element = request.Element;
        if (IsLeftEdgeResize(request))
            return (true, element.Start, request.NewStart);
        if (request.NewStart == element.Start && request.NewLength != element.Length)
            return (false, element.Range.End, request.NewStart + request.NewLength);
        return null;
    }

    private static ElementResizeRequest[] ClampSharedResizeEdges(IReadOnlyList<ElementResizeRequest> requests,
        IReadOnlyList<ResizePeerGroup> groups, Dictionary<Element, SlippableMedia.ResizeConstraints> constraints)
    {
        ElementResizeRequest[] result = requests.ToArray();
        foreach (ResizePeerGroup group in groups)
        {
            var limits = group.Elements.Select(element => constraints.GetValueOrDefault(element))
                .OfType<SlippableMedia.ResizeConstraints>().ToArray();
            if (!limits.Any(limit => limit.HasSharedClock)) continue;
            int[] indices = Enumerable.Range(0, result.Length).Where(i => group.Elements.Contains(result[i].Element)).ToArray();
            MediaTime delta = indices.Select(i => group.LeftEdge
                    ? result[i].NewStart - result[i].Element.Start
                    : result[i].NewLength - result[i].Element.Length)
                .MinBy(value => Math.Abs(value.Flicks));
            delta = SlippableMedia.ClampSharedResizeDelta(limits, delta, group.LeftEdge);
            foreach (int i in indices)
            {
                Element element = result[i].Element;
                result[i] = group.LeftEdge
                    ? result[i] with { NewStart = element.Start + delta, NewLength = element.Length - delta }
                    : result[i] with { NewLength = element.Length + delta };
            }
        }
        return result;
    }

    private static void ExtendSceneDurationToIncludeChildren(Scene scene)
    {
        MediaTime sceneEnd = scene.Start + scene.Duration;
        foreach (Element child in scene.Children)
        {
            if (sceneEnd < child.Range.End)
            {
                sceneEnd = child.Range.End;
            }
        }

        scene.Duration = sceneEnd - scene.Start;
    }

    private static ElementResizeRequest ClampMedia(ElementResizeRequest req,
        SlippableMedia.ResizeConstraints constraints, MediaTime minLength)
    {
        if (IsLeftEdgeResize(req))
        {
            MediaTime start = constraints.ClampStart(req.NewStart);
            return req with { NewStart = start, NewLength = req.Element.Range.End - start };
        }
        MediaTime length = constraints.ClampLength(req.NewLength, req.NewStart);
        return req with { NewLength = length < minLength ? minLength : length };
    }

    private static ElementResizeRequest[] NormalizeRequests(IReadOnlyList<ElementResizeRequest> requests, bool ripple, MediaTime minLength)
    {
        var normalized = new ElementResizeRequest[requests.Count];
        for (int i = 0; i < requests.Count; i++)
        {
            ElementResizeRequest req = requests[i];
            MediaTime start = req.NewStart < MediaTime.Zero ? MediaTime.Zero : req.NewStart;
            MediaTime length = req.NewLength;
            if (ripple && req.NewStart < MediaTime.Zero && length > MediaTime.Zero)
            {
                // Preserve the requested end so clamping a left-edge drag does not ripple followers.
                length += req.NewStart;
            }

            if (length < minLength)
            {
                // A changed start with the original end identifies a fixed-right-edge resize.
                MediaTime end = req.Element.Range.End;
                if (req.NewStart >= MediaTime.Zero && req.NewStart != req.Element.Start
                    && req.NewLength == end - req.NewStart)
                {
                    start = end > minLength ? end - minLength : MediaTime.Zero;
                }

                length = minLength;
            }
            normalized[i] = req with { NewStart = start, NewLength = length };
        }

        return normalized;
    }

    // Limits a same-layer left-edge grow so the rigid ripple shift cannot push any upstream element
    // below zero or onto a locked clip, keeping the requested end. Clamps rather than throws: UI
    // callers run on an async-void pointer path, and frame rounding at submission can dip below the
    // preview floor.
    private static (MediaTime Start, MediaTime Length) ClampRippleStart(
        Scene scene, ElementResizeRequest req, IReadOnlyCollection<Element> resized, MediaTime minLength)
    {
        if (req.ZIndex != req.Element.ZIndex) return (req.NewStart, req.NewLength);

        MediaTime startDelta = req.NewStart - req.Element.Start;
        if (startDelta >= MediaTime.Zero) return (req.NewStart, req.NewLength);

        // Collect the layer's locked-clip ends once and sort them; a locked clip is an immovable
        // barrier the upstream shift cannot cross, so each clip's floor is one binary search.
        var lockedEnds = new List<MediaTime>();
        foreach (Element e in scene.Children)
        {
            if (e.ZIndex == req.Element.ZIndex && scene.IsElementLocked(e)) lockedEnds.Add(e.Range.End);
        }

        lockedEnds.Sort();

        // Every upstream clip shifts left by the same delta, so the grow is bounded by the tightest
        // room: each clip can move left only to the timeline start or the end of the nearest locked
        // clip in front of it (locked clips are immovable and must not be overlapped). Seed the bound
        // with the resized clip's own room so its left edge is clamped even with no free upstream clip.
        MediaTime maxGrow = req.Element.Start - NearestLockedEndAtOrBefore(lockedEnds, req.Element.Start);
        foreach (Element e in scene.Children)
        {
            if (e.ZIndex != req.Element.ZIndex || resized.Contains(e) || scene.IsElementLocked(e)
                || e.Range.End > req.Element.Start)
            {
                continue;
            }

            MediaTime room = e.Start - NearestLockedEndAtOrBefore(lockedEnds, e.Start);
            if (room < maxGrow) maxGrow = room;
        }

        if (startDelta >= -maxGrow)
        {
            return (req.NewStart, req.NewLength);
        }

        MediaTime clampedStart = req.Element.Start - maxGrow;
        MediaTime clampedLength = req.NewStart + req.NewLength - clampedStart;
        // If the requested end is too close to or before the barrier, keep a positive minimum.
        if (clampedLength < minLength) clampedLength = minLength;

        return (clampedStart, clampedLength);
    }

    // Largest end in the sorted list that is at or before start, or zero (the timeline floor) when
    // none qualifies.
    private static MediaTime NearestLockedEndAtOrBefore(List<MediaTime> sortedEnds, MediaTime start)
    {
        int lo = 0, hi = sortedEnds.Count - 1;
        MediaTime floor = MediaTime.Zero;
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
    private static MediaTime ClampRippleEnd(
        Scene scene, ElementResizeRequest req, MediaTime start, MediaTime length, IReadOnlyCollection<Element> resized)
    {
        if (req.ZIndex != req.Element.ZIndex) return length;

        MediaTime oldEnd = req.Element.Range.End;
        MediaTime newEnd = start + length;
        if (newEnd <= oldEnd) return length;

        MediaTime? nearestLockedStart = null;
        foreach (Element e in scene.Children)
        {
            if (e.ZIndex == req.Element.ZIndex && scene.IsElementLocked(e) && e.Start >= oldEnd)
            {
                if (nearestLockedStart is not { } cur || e.Start < cur) nearestLockedStart = e.Start;
            }
        }

        if (nearestLockedStart is not { } lockStart) return length;

        MediaTime blockingEnd = oldEnd;
        foreach (Element e in scene.Children)
        {
            if (e.ZIndex == req.Element.ZIndex && !scene.IsElementLocked(e) && !resized.Contains(e)
                && e.Start >= oldEnd && e.Start < lockStart && e.Range.End > blockingEnd)
            {
                blockingEnd = e.Range.End;
            }
        }

        MediaTime maxEnd = oldEnd + (lockStart - blockingEnd);
        if (newEnd <= maxEnd) return length;

        MediaTime clampedEnd = maxEnd > oldEnd ? maxEnd : oldEnd;
        // A start past the clamp point (e.g. the element also moved right onto the lock) would make
        // the clamped length non-positive and corrupt the range; leave the length to the caller's
        // move validation instead of forcing a bad value here.
        MediaTime clampedLength = clampedEnd - start;
        return clampedLength > MediaTime.Zero ? clampedLength : length;
    }
}
