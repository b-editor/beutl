using Beutl.Audio;
using Beutl.Media.Source;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class SourceSoundOperator : StyledSourcePublisher
{
    public Setter<ISoundSource?> Source { get; set; }
        = new Setter<ISoundSource?>(SourceSound.SourceProperty, null);

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

    protected override void OnAfterApplying()
    {
        base.OnAfterApplying();
        if (Instance?.Target is SourceSound sound)
        {
            sound.Effect = null;
            sound.Gain = 1;
        }
    }
}
