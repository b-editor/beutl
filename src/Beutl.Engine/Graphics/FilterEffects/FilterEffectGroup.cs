using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

public sealed class FilterEffectGroup : FilterEffect
{
    public static readonly CoreProperty<FilterEffects> ChildrenProperty;
    private readonly FilterEffects _children;

    static FilterEffectGroup()
    {
        ChildrenProperty = ConfigureProperty<FilterEffects, FilterEffectGroup>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .Register();
    }

    public FilterEffectGroup()
    {
        _children = [];
        _children.Invalidated += (_, e) => RaiseInvalidated(e);
    }

    [NotAutoSerialized]
    public FilterEffects Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Children), Children);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if(context.GetValue<FilterEffects>(nameof(Children)) is { } children)
        {
            Children = children;
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        foreach (FilterEffect item in Children.GetMarshal().Value)
        {
            (item as IAnimatable)?.ApplyAnimations(clock);
        }
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        foreach (FilterEffect item in _children.GetMarshal().Value)
        {
            context.Apply(item);
        }
    }

    public override Rect TransformBounds(Rect bounds)
    {
        foreach (FilterEffect item in _children.GetMarshal().Value)
        {
            if (item.IsEnabled)
                bounds = item.TransformBounds(bounds);
        }

        return bounds;
    }
}
