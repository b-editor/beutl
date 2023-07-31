using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Operation;

namespace Beutl.Operators.Configure.SoundEffect;

#pragma warning disable IDE0065
using SoundEffect = Audio.Effects.SoundEffect;

public abstract class SoundEffectOperator<T> : ConfigureOperator<Sound, T>, ISourceTransformer
    where T : SoundEffect, new()
{
    protected override void PreProcess(Sound target, T value)
    {
        value.IsEnabled = IsEnabled;
    }

    protected override void Process(Sound target, T value)
    {
        (target.Effect as SoundEffectGroup)?.Children.Add(value);
    }
}
