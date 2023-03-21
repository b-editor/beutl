using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

    protected override void OnAttached(Sound target, T value)
    {
        if (target.Effect is not SoundEffectGroup group)
        {
            target.Effect = group = new SoundEffectGroup();
        }

        group.Children.Add(value);
    }

    protected override void OnDetached(Sound target, T value)
    {
        if (target.Effect is SoundEffectGroup group)
        {
            group.Children.Remove(value);
        }
    }
}
