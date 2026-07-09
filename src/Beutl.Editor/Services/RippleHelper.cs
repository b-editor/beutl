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

        Element[] toShift = scene.Children
            .Where(e => e.ZIndex == zIndex && !except.Contains(e) && e.Start >= anchorEnd)
            .ToArray();

        foreach (Element e in toShift)
        {
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

        Element[] toShift = scene.Children
            .Where(e => e.ZIndex == zIndex && !except.Contains(e) && e.Range.End <= anchorStart)
            .ToArray();

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
