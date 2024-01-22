using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Media.Source;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class SourceSoundOperator : StyledSourcePublisher
{
    public Setter<ISoundSource?> Source { get; set; }
        = new Setter<ISoundSource?>(SourceSound.SourceProperty, null);

    public Setter<TimeSpan> OffsetPosition { get; set; }
        = new Setter<TimeSpan>(SourceSound.OffsetPositionProperty, TimeSpan.Zero);

    public Setter<float> Gain { get; set; }
        = new Setter<float>(Sound.GainProperty, 100);

    public Setter<ISoundEffect?> Effect { get; set; }
        = new Setter<ISoundEffect?>(Sound.EffectProperty, new SoundEffectGroup());

    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<SourceSound>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnBeforeApplying()
    {
        base.OnBeforeApplying();
        if (Instance?.Target is SourceSound sound)
        {
            sound.BeginBatchUpdate();
        }
    }

    public override bool HasOriginalLength()
    {
        return Source.Value?.IsDisposed == false;
    }

    public override bool TryGetOriginalLength(out TimeSpan timeSpan)
    {
        if (Source.Value?.IsDisposed == false)
        {
            timeSpan = Source.Value.Duration;
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
        if (backward)
        {
            IStorable? storable = this.FindHierarchicalParent<IStorable>();
            TimeSpan newValue = OffsetPosition.Value + startDelta;
            TimeSpan oldValue = OffsetPosition.Value;

            return RecordableCommands.Create([storable])
                .OnDo(() => OffsetPosition.Value = newValue)
                .OnUndo(() => OffsetPosition.Value = oldValue)
                .ToCommand();
        }
        else
        {
            return base.OnSplit(backward, startDelta, lengthDelta);
        }
    }
}
