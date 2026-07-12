using Beutl.Engine;
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
        var applicable = new List<(List<SlippableMedia.Target> Targets, TimeSpan Length)>();
        foreach (Element element in elements)
        {
            if (!seen.Add(element)) continue;
            if (!scene.Children.Contains(element)) continue;
            if (scene.IsElementLocked(element)) continue;

            List<SlippableMedia.Target> targets = SlippableMedia.Collect(element);
            if (targets.Count == 0) continue;

            applicable.Add((targets, element.Length));
        }

        if (applicable.Count == 0) return false;

        // Chained clamping: each element can only shrink the magnitude, so the final value is
        // the delta every stream of every element can absorb — grouped linked media stay in sync.
        TimeSpan effective = delta;
        foreach ((List<SlippableMedia.Target> targets, TimeSpan length) in applicable)
        {
            effective = SlippableMedia.ClampSharedDelta(targets, effective, length);
            if (effective == TimeSpan.Zero) return false;
        }

        var applied = new HashSet<IProperty<TimeSpan>>();
        foreach ((List<SlippableMedia.Target> targets, _) in applicable)
        {
            SlippableMedia.ApplyOffsetDelta(targets, effective, applied);
        }

        _historyManager.Commit(CommandNames.SlipElement);
        return true;
    }
}
