using Beutl.Audio.Effects;
using Beutl.Styling;

namespace Beutl.Operators.Configure.SoundEffect;

public sealed class DelayOperator : SoundEffectOperator<Delay>
{
    public Setter<float> DelayTime { get; set; } = new(Delay.DelayTimeProperty, 0.2f);

    public Setter<float> Feedback { get; set; } = new(Delay.FeedbackProperty, 0.5f);

    public Setter<float> DryMix { get; set; } = new(Delay.DryMixProperty, 0.6f);

    public Setter<float> WetMix { get; set; } = new(Delay.WetMixProperty, 0.4f);
}
