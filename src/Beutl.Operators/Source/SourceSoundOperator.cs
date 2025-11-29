using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Media.Source;
using Beutl.Operation;

namespace Beutl.Operators.Source;

public sealed class SourceSoundOperator : PublishOperator<SourceSound>
{
    private Uri? _uri;

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

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        if (Value is not { Source.CurrentValue: { Uri: { } uri } source } value) return;

        _uri = uri;
        value.Source.CurrentValue = null;
        source.Dispose();
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        if (_uri is null) return;
        if (Value is not { } value) return;

        if (SoundSource.TryOpen(_uri, out SoundSource? source))
        {
            value.Source.CurrentValue = source;
        }
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
