using Beutl.Audio.Effects;

namespace Beutl.Operators.Configure.SoundEffect;

public sealed class DelayOperator : SoundEffectOperator<Delay>
{
    protected override IEnumerable<CoreProperty> GetProperties()
    {
        yield return Delay.DelayTimeProperty;
        yield return Delay.FeedbackProperty;
        yield return Delay.DryMixProperty;
        yield return Delay.WetMixProperty;
    }
}
