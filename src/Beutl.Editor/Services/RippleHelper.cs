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

        IEnumerable<Element> toShift = scene.Children
            .Where(e => e.ZIndex == zIndex && !except.Contains(e) && e.Range.End <= anchorStart
                        && !e.IsLocked);

        Element[] lockedOnLayer = scene.Children
            .Where(e => e.ZIndex == zIndex && e.IsLocked)
            .ToArray();

        // A locked clip is an immovable anchor in both directions, so the ripple stops before any
        // clip lands on it. Process in travel order — rightmost first for a right pull (delta > 0),
        // leftmost first for a left push (delta < 0) — so a block also halts the clips behind it.
        // The timeline floor for a left push is enforced by the caller's ClampRippleStart.
        toShift = delta > TimeSpan.Zero
            ? toShift.OrderByDescending(e => e.Start)
            : toShift.OrderBy(e => e.Start);

        foreach (Element e in toShift)
        {
            TimeRange shifted = e.Range.WithStart(e.Start + delta);
            if (Array.Exists(lockedOnLayer, l => shifted.Intersects(l.Range))) break;

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
