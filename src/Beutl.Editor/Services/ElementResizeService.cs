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

    // Limits a same-layer left-edge grow so the ripple shift cannot push any upstream element's
    // Start below zero, keeping the requested end. Clamps rather than throws: UI callers run on an
    // async-void pointer path, and frame rounding at submission can dip below the preview floor.
    private static (TimeSpan Start, TimeSpan Length) ClampRippleStart(
        Scene scene, ElementResizeRequest req, IReadOnlyCollection<Element> resized)
    {
        if (req.ZIndex != req.Element.ZIndex) return (req.NewStart, req.NewLength);

        TimeSpan startDelta = req.NewStart - req.Element.Start;
        if (startDelta >= TimeSpan.Zero) return (req.NewStart, req.NewLength);

        // Match ShiftBefore's eligibility: a locked upstream element is never shifted,
        // so it must not tighten the floor (else it would clamp a valid left-edge grow).
        TimeSpan? minUpstreamStart = null;
        foreach (Element e in scene.Children)
        {
            if (e.ZIndex == req.Element.ZIndex && !resized.Contains(e) && !e.IsLocked
                && e.Range.End <= req.Element.Start)
            {
                if (minUpstreamStart is not { } cur || e.Start < cur) minUpstreamStart = e.Start;
            }
        }

        if (minUpstreamStart is not { } floor || startDelta >= -floor)
        {
            return (req.NewStart, req.NewLength);
        }

        TimeSpan clampedStart = req.Element.Start - floor;
        TimeSpan clampedLength = req.NewStart + req.NewLength - clampedStart;
        if (clampedLength <= TimeSpan.Zero)
        {
            // Requested end sits before the floor: keeping upstream >= 0 and the end while staying
            // positive is impossible. A left-edge resize keeps the end, so the UI never reaches this.
            throw new ArgumentOutOfRangeException(
                nameof(ElementResizeRequest.NewLength),
                "Ripple left-shift cannot preserve the requested end with a positive length.");
        }

        return (clampedStart, clampedLength);
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
        return clampedEnd - start;
    }
}
