using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
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

        bool applied = false;
        foreach (EngineObject obj in element.Objects)
        {
            if (obj is SourceVideo sv)
            {
                sv.OffsetPosition.CurrentValue += delta;
                applied = true;
            }
            else if (obj is Sound sound)
            {
                sound.OffsetPosition.CurrentValue += delta;
                applied = true;
            }
        }

        if (applied)
        {
            _historyManager.Commit(CommandNames.SlipElement);
        }
        return applied;
    }
}
