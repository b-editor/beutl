using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.Graphics.Effects;
using BeUtl.Rendering;
using BeUtl.Streaming;
using BeUtl.Styling;

namespace BeUtl.Operators.Configure.BitmapEffect;

#pragma warning disable IDE0065
using BitmapEffect = Graphics.Effects.BitmapEffect;

public abstract class BitmapEffectOperator : StreamStyler
{
    private Drawable? _previous;
    private BitmapEffect? _instance;

    protected override IStyleInstance? GetInstance(IRenderable value)
    {
        if (!ReferenceEquals(_previous, value))
        {
            if (Style.TargetType.IsAssignableTo(typeof(BitmapEffect)))
            {
                return Style.Instance(CreateTargetValue(Style.TargetType));
            }
            else
            {
                return null;
            }
        }
        else
        {
            return Instance;
        }
    }

    protected override void ApplyStyle(IStyleInstance instance, IRenderable value, IClock clock)
    {
        if (value is Drawable current && instance.Target is BitmapEffect target)
        {
            target.IsEnabled = IsEnabled;
            if (_previous != current)
            {
                IBitmapEffect? tmp = current.Effect;
                if (current.Effect is not BitmapEffectGroup group)
                {
                    current.Effect = group = new BitmapEffectGroup();
                    if (tmp != null)
                    {
                        group.Children.Add(tmp);
                    }
                }

                if (_previous?.Effect is BitmapEffectGroup group1)
                {
                    group1.Children.Remove(target);
                }

                group.Children.Add(target);
                _previous = current;
                _instance = target;
            }

            instance.Begin();
            instance.Apply(clock);
            instance.End();
        }
    }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        if (_previous != null
            && _previous.Effect is BitmapEffectGroup group
            && _instance != null)
        {
            group.Children.Remove(_instance);
        }
    }

    private static BitmapEffect CreateTargetValue(Type type)
    {
        return (BitmapEffect)Activator.CreateInstance(type)!;
    }
}
