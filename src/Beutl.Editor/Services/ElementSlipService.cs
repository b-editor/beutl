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

    public bool Slip(Element element, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (delta == TimeSpan.Zero) return false;

        List<SlippableMedia.Target> targets = SlippableMedia.Collect(element);
        if (targets.Count == 0) return false;

        TimeSpan effective = SlippableMedia.ClampSharedDelta(targets, delta, element.Length);
        if (effective == TimeSpan.Zero) return false;

        SlippableMedia.ApplyOffsetDelta(targets, effective);
        _historyManager.Commit(CommandNames.SlipElement);
        return true;
    }
}
