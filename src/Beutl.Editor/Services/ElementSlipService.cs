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
                applied |= ShiftOffset(sv, delta, element.Length);
            }
            else if (obj is Sound sound)
            {
                applied |= ShiftSound(sound, delta, element.Length);
            }
        }

        if (applied)
        {
            _historyManager.Commit(CommandNames.SlipElement);
        }
        return applied;
    }

    private static bool ShiftOffset(SourceVideo source, TimeSpan delta, TimeSpan elementLength)
    {
        TimeSpan current = source.OffsetPosition.CurrentValue;
        TimeSpan? maxOffset = null;
        if (source.TryGetOriginalDuration(out TimeSpan remainingDuration))
        {
            maxOffset = GetMaxOffset(current, remainingDuration, elementLength);
        }

        TimeSpan next = ClampOffset(current + delta, maxOffset);
        if (next == current) return false;

        source.OffsetPosition.CurrentValue = next;
        return true;
    }

    private static bool ShiftSound(Sound sound, TimeSpan delta, TimeSpan elementLength)
    {
        return sound switch
        {
            SourceSound sourceSound => ShiftOffset(sourceSound, delta, elementLength),
            SoundGroup group => ShiftSoundGroup(group, delta, elementLength),
            _ => false
        };
    }

    private static bool ShiftSoundGroup(SoundGroup group, TimeSpan delta, TimeSpan elementLength)
    {
        bool applied = false;
        foreach (Sound child in group.Children)
        {
            applied |= ShiftSound(child, delta, elementLength);
        }

        return applied;
    }

    private static bool ShiftOffset(SourceSound sound, TimeSpan delta, TimeSpan elementLength)
    {
        TimeSpan current = sound.OffsetPosition.CurrentValue;
        TimeSpan? maxOffset = null;
        if (sound.TryGetOriginalDuration(out TimeSpan originalDuration))
        {
            maxOffset = GetMaxOffset(TimeSpan.Zero, originalDuration, elementLength);
        }

        TimeSpan next = ClampOffset(current + delta, maxOffset);
        if (next == current) return false;

        sound.OffsetPosition.CurrentValue = next;
        return true;
    }

    private static TimeSpan GetMaxOffset(TimeSpan currentOffset, TimeSpan remainingDuration, TimeSpan elementLength)
    {
        TimeSpan maxOffset = currentOffset + remainingDuration - elementLength;
        return maxOffset < TimeSpan.Zero ? TimeSpan.Zero : maxOffset;
    }

    private static TimeSpan ClampOffset(TimeSpan value, TimeSpan? maxOffset)
    {
        if (value < TimeSpan.Zero)
            return TimeSpan.Zero;

        if (maxOffset.HasValue && value > maxOffset.Value)
            return maxOffset.Value;

        return value;
    }
}
