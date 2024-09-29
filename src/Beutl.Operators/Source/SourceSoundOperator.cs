using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Media.Source;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class SourceSoundOperator() : PublishOperator<SourceSound>(
[
    SourceSound.SourceProperty,
    (SourceSound.OffsetPositionProperty, TimeSpan.Zero),
    (Sound.GainProperty, 100f),
    (Sound.EffectProperty, () => new SoundEffectGroup())
])
{
    public override bool HasOriginalLength()
    {
        return Value?.Source?.IsDisposed == false;
    }

    public override bool TryGetOriginalLength(out TimeSpan timeSpan)
    {
        if (Value?.Source?.IsDisposed == false)
        {
            timeSpan = Value.Source.Duration;
            return true;
        }
        else
        {
            timeSpan = TimeSpan.Zero;
            return false;
        }
    }

    public override IRecordableCommand? OnSplit(bool backward, TimeSpan startDelta, TimeSpan lengthDelta)
    {
        if (Value is null) return null;

        if (backward)
        {
            IStorable? storable = this.FindHierarchicalParent<IStorable>();
            TimeSpan newValue = Value.OffsetPosition + startDelta;
            TimeSpan oldValue = Value.OffsetPosition;

            return RecordableCommands.Create([storable])
                .OnDo(() => Value.OffsetPosition = newValue)
                .OnUndo(() => Value.OffsetPosition = oldValue)
                .ToCommand();
        }
        else
        {
            return base.OnSplit(backward, startDelta, lengthDelta);
        }
    }
}
