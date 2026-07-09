using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

// Each call must run inside the caller's open history transaction so the
// shift records into the same undo entry as the triggering operation.
internal static class RippleHelper
{
    public static void ShiftAfter(
        Scene scene,
        int zIndex,
        TimeSpan anchorEnd,
        TimeSpan delta,
        IReadOnlyCollection<Element> except)
    {
        if (delta == TimeSpan.Zero) return;

        // A locked follower stays anchored — shifting it would bypass the lock.
        if (scene.IsLayerLocked(zIndex)) return;

        Element[] lockedOnLayer = scene.Children
            .Where(e => e.ZIndex == zIndex && e.IsLocked)
            .ToArray();

        Element[] candidates = scene.Children
            .Where(e => e.ZIndex == zIndex && !except.Contains(e) && !e.IsLocked && e.Start >= anchorEnd)
            .OrderBy(e => e.Start)
            .ToArray();

        foreach (Element e in candidates)
        {
            TimeRange shifted = e.Range.WithStart(e.Start + delta);
            if (Array.Exists(lockedOnLayer, l => shifted.Intersects(l.Range))) break;

            e.Start += delta;
        }
    }

    public static void ShiftBefore(
        Scene scene,
        int zIndex,
        TimeSpan anchorStart,
        TimeSpan delta,
        IReadOnlyCollection<Element> except)
    {
        if (delta == TimeSpan.Zero) return;

        // A locked neighbor stays anchored — shifting it would bypass the lock.
        if (scene.IsLayerLocked(zIndex)) return;

        Element[] toShift = scene.Children
            .Where(e => e.ZIndex == zIndex && !except.Contains(e) && e.Range.End <= anchorStart
                        && !e.IsLocked)
            .ToArray();

        // A left-edge trim (delta > 0) pulls upstream clips right onto a locked anchor, so it must
        // stop at the first blocker (nearest-to-anchor first, halting the clips behind it). A
        // left-edge grow (delta < 0) pushes them left, where the timeline floor is the caller's
        // ClampRippleStart and locked clips are already out of the shift set.
        if (delta > TimeSpan.Zero)
        {
            Element[] lockedOnLayer = scene.Children
                .Where(e => e.ZIndex == zIndex && e.IsLocked)
                .ToArray();

            foreach (Element e in toShift.OrderByDescending(e => e.Start))
            {
                TimeRange shifted = e.Range.WithStart(e.Start + delta);
                if (Array.Exists(lockedOnLayer, l => shifted.Intersects(l.Range))) break;

                e.Start += delta;
            }

            return;
        }

        foreach (Element e in toShift)
        {
            e.Start += delta;
        }
    }

    // removed carries each removed element's pre-removal (ZIndex, End, Length),
    // because Scene.RemoveChild mutates Element.ZIndex to -1.
    // OrderByDescending(End) is load-bearing: processing right-to-left keeps
    // non-contiguous multi-element ripple correct.
    public static void ShiftAfterRemoved(
        Scene scene,
        IReadOnlyList<(int ZIndex, TimeSpan End, TimeSpan Length)> removed)
    {
        foreach (var r in removed.OrderByDescending(r => r.End))
        {
            ShiftAfter(scene, r.ZIndex, r.End, -r.Length, Array.Empty<Element>());
        }
    }

    public static void RemoveAndShiftAfter(
        Scene scene,
        IReadOnlyList<Element> elements,
        bool ripple,
        Action<Element> removeElement)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(removeElement);

        (int ZIndex, TimeSpan End, TimeSpan Length)[] removed = ripple
            ? elements.Select(e => (e.ZIndex, e.Range.End, e.Length)).ToArray()
            : [];

        foreach (Element element in elements.ToArray())
        {
            removeElement(element);
        }

        if (ripple)
        {
            ShiftAfterRemoved(scene, removed);
        }
    }
}
