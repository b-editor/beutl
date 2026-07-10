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

    public bool Slip(Scene scene, Element element, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(element);
        if (delta == TimeSpan.Zero) return false;
        // Re-check membership and lock at the mutation boundary (matching Roll/Slide): a direct
        // caller may pass an off-scene element, and the clip or its layer may have been locked
        // after the drag began, so the press-time IsEditable gate is not enough.
        if (!scene.Children.Contains(element)) return false;
        if (scene.IsElementLocked(element)) return false;

        List<SlippableMedia.Target> targets = SlippableMedia.Collect(element);
        if (targets.Count == 0) return false;

        TimeSpan effective = SlippableMedia.ClampSharedDelta(targets, delta, element.Length);
        if (effective == TimeSpan.Zero) return false;

        SlippableMedia.ApplyOffsetDelta(targets, effective);
        _historyManager.Commit(CommandNames.SlipElement);
        return true;
    }
}
