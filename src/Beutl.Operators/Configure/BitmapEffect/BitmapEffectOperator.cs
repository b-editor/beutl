using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Operation;

namespace Beutl.Operators.Configure.BitmapEffect;

#pragma warning disable IDE0065
using BitmapEffect = Graphics.Effects.BitmapEffect;

public abstract class BitmapEffectOperator<T> : ConfigureOperator<Drawable, T>, ISourceTransformer
    where T : BitmapEffect, new()
{
    protected override void PreProcess(Drawable target, T value)
    {
        value.IsEnabled = IsEnabled;
    }

    protected override void Process(Drawable target, T value)
    {
        (target.Effect as BitmapEffectGroup)?.Children.Add(value);
    }
}
