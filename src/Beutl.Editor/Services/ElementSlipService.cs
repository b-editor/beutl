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
                applied |= ShiftOffset(sv, delta);
            }
            else if (obj is Sound sound)
            {
                applied |= ShiftOffset(sound, delta);
            }
        }

        if (applied)
        {
            _historyManager.Commit(CommandNames.SlipElement);
        }
        return applied;
    }

    private static bool ShiftOffset(SourceVideo source, TimeSpan delta)
    {
        TimeSpan current = source.OffsetPosition.CurrentValue;
        TimeSpan next = ClampOffset(current + delta);
        if (next == current) return false;

        source.OffsetPosition.CurrentValue = next;
        return true;
    }

    private static bool ShiftOffset(Sound sound, TimeSpan delta)
    {
        TimeSpan current = sound.OffsetPosition.CurrentValue;
        TimeSpan next = ClampOffset(current + delta);
        if (next == current) return false;

        sound.OffsetPosition.CurrentValue = next;
        return true;
    }

    private static TimeSpan ClampOffset(TimeSpan value)
    {
        return value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }
}
