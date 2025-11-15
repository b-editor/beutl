using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Operation;

namespace Beutl.Operators.Source;

public sealed class SourceSoundOperator : PublishOperator<SourceSound>
{
    public override bool HasOriginalLength()
    {
        return Value?.Source.CurrentValue?.IsDisposed == false;
    }

    protected override void FillProperties()
    {
        AddProperty(Value.Source);
        AddProperty(Value.OffsetPosition, TimeSpan.Zero);
        AddProperty(Value.Gain, 100f);
        AddProperty(Value.Speed, 100f);
        AddProperty(Value.Effect, new AudioEffectGroup());
    }

    public override bool TryGetOriginalLength(out TimeSpan timeSpan)
    {
        if (Value?.Source.CurrentValue?.IsDisposed == false)
        {
            timeSpan = Value.Source.CurrentValue.Duration;
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
            TimeSpan newValue = Value.OffsetPosition.CurrentValue + startDelta;
            TimeSpan oldValue = Value.OffsetPosition.CurrentValue;

            return RecordableCommands.Create([this])
                .OnDo(() => Value.OffsetPosition.CurrentValue = newValue)
                .OnUndo(() => Value.OffsetPosition.CurrentValue = oldValue)
                .ToCommand();
        }
        else
        {
            return base.OnSplit(backward, startDelta, lengthDelta);
        }
    }
}
