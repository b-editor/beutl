using Beutl.Audio.Effects;
using Beutl.Styling;

namespace Beutl.Operators.Configure.SoundEffect;

public sealed class DelayOperator : SoundEffectOperator<Delay>
{
    public Setter<float> DelayTime { get; set; } = new(Delay.DelayTimeProperty, 200f);

    public Setter<float> Feedback { get; set; } = new(Delay.FeedbackProperty, 50f);

    public Setter<float> DryMix { get; set; } = new(Delay.DryMixProperty, 60f);

    public Setter<float> WetMix { get; set; } = new(Delay.WetMixProperty, 40f);
}
