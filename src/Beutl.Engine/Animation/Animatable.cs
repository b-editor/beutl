using System.ComponentModel;
using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Animation;

public abstract class Animatable : CoreObject, IAnimatable
{
    public static readonly CoreProperty<Animations> AnimationsProperty;

    static Animatable()
    {
        AnimationsProperty = ConfigureProperty<Animations, Animatable>(nameof(Animations))
            .Accessor(o => o.Animations)
            .RegisterStatic();
    }

    protected Animatable()
    {
        Animations = [];
    }

    [NotAutoSerialized]
    [Browsable(false)]
    public Animations Animations { get; }

    public event EventHandler<RenderInvalidatedEventArgs> AnimationInvalidated
    {
        add => Animations.Invalidated += value;
        remove => Animations.Invalidated -= value;
    }

    public virtual void ApplyAnimations(IClock clock)
    {
        foreach (IAnimation? item in Animations.GetMarshal().Value)
        {
            item.ApplyAnimation(this, clock);
        }
    }

    // NOTE: 互換性のため、JsonArrayではなくJsonObjectになるようにしている
    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Animations), Animations.ToDictionary(x => x.Property.Name, y => y));
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        Dictionary<string, IAnimation>? animations
            = context.GetValue<Dictionary<string, IAnimation>>(nameof(Animations));
        animations ??= context.GetValue<Dictionary<string, IAnimation>>("animations");

        if (animations != null)
        {
            Animations.Clear();
            Animations.EnsureCapacity(animations.Count);

            Type type = GetType();
            foreach (KeyValuePair<string, IAnimation> item in animations)
            {
                if (item.Value is KeyFrameAnimation { Property: null } kfAnim)
                {
                    kfAnim.Property = PropertyRegistry.GetRegistered(type).FirstOrDefault(x => x.Name == item.Key)!;
                }

                Animations.Add(item.Value);
            }
        }
    }
}
