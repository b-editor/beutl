using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class ElementSlipService : IElementSlipService
{
    private readonly HistoryManager _historyManager;

    public ElementSlipService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public bool Slip(Scene scene, IReadOnlyList<Element> elements, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);
        foreach (Element element in elements)
        {
            if (element is null)
                throw new ArgumentNullException(nameof(elements), "elements must not contain null.");
        }

        if (delta == TimeSpan.Zero) return false;

        // Re-check membership and lock at the mutation boundary (matching Resize): a direct
        // caller may pass an off-scene element, and a clip or its layer may have been locked
        // after the drag began, so the press-time IsEditable gate is not enough. Disqualified
        // members are dropped rather than blocking the rest of the group.
        var seen = new HashSet<Element>();
        var applicable = new List<SlippableMedia.Target>();
        foreach (Element element in elements)
        {
            if (!seen.Add(element)) continue;
            if (!scene.Children.Contains(element)) continue;
            if (scene.IsElementLocked(element)) continue;

            List<SlippableMedia.Target> targets = SlippableMedia.Collect(element);
            if (targets.Count == 0) continue;

            applicable.AddRange(targets);
        }

        if (applicable.Count == 0) return false;

        TimeSpan effective = SlippableMedia.ClampSharedDelta(applicable, delta);
        if (effective == TimeSpan.Zero) return false;

        if (!SlippableMedia.TryGetOffsetChanges(applicable, effective, trim: false, out var changes)
            || changes.Values.All(d => d == TimeSpan.Zero)) return false;
        SlippableMedia.ApplyOffsetChanges(changes);

        _historyManager.Commit(CommandNames.SlipElement);
        return true;
    }
}
