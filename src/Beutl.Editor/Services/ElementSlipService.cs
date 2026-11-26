using Beutl.Engine;
using Beutl.Language;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class ElementSlipService : IElementSlipService
{
    private readonly HistoryManager _historyManager;
    private readonly DecoderRegistry? _decoders;

    public ElementSlipService(HistoryManager historyManager, DecoderRegistry? decoders = null)
    {
        _decoders = decoders;
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public bool Slip(Scene scene, IReadOnlyList<Element> elements, MediaTime delta)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);
        foreach (Element element in elements)
        {
            if (element is null)
                throw new ArgumentNullException(nameof(elements), "elements must not contain null.");
        }

        if (delta == MediaTime.Zero) return false;

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

            List<SlippableMedia.Target> targets = SlippableMedia.Collect(element, decoders: _decoders);
            if (targets.Count == 0) continue;

            applicable.AddRange(targets);
        }

        if (PlanSlip(applicable, delta) is not { } plan) return false;
        SlippableMedia.ApplyOffsetChanges(plan.Changes);

        _historyManager.Commit(CommandNames.SlipElement);
        return true;
    }

    internal static (MediaTime Delta, Dictionary<IProperty<MediaTime>, MediaTime> Changes)? PlanSlip(
        IReadOnlyList<SlippableMedia.Target> targets, MediaTime delta)
    {
        if (delta == MediaTime.Zero || targets.Count == 0) return null;
        MediaTime effective = SlippableMedia.ClampSharedDelta(targets, delta);
        if (effective == MediaTime.Zero
            || !SlippableMedia.TryGetOffsetChanges(targets, effective, trim: false, out var changes)
            || changes.Values.All(change => change == MediaTime.Zero)) return null;

        return (effective, changes);
    }
}
