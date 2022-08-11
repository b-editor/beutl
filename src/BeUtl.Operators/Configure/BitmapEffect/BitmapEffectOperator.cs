using BeUtl.Graphics;
using BeUtl.Graphics.Effects;
using BeUtl.Streaming;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.BitmapEffect;

#pragma warning disable IDE0065
using BitmapEffect = Graphics.Effects.BitmapEffect;

public abstract class BitmapEffectOperator<T> : ConfigureOperator<Drawable, T>, IStreamSelector
    where T : BitmapEffect, new()
{
    protected override void PreSelect(Drawable target, T value)
    {
        value.IsEnabled = IsEnabled;
    }

    protected override void OnAttached(Drawable target, T value)
    {
        IBitmapEffect? tmp = target.Effect;
        if (target.Effect is not BitmapEffectGroup group)
        {
            target.Effect = group = new BitmapEffectGroup();
            if (tmp != null)
            {
                group.Children.Add(tmp);
            }
        }

        group.Children.Add(value);
    }

    protected override void OnDetached(Drawable target, T value)
    {
        if (target.Effect is BitmapEffectGroup group)
        {
            group.Children.Remove(value);
        }
        else if (target.Effect == value)
        {
            target.Effect = null;
        }
    }
}
