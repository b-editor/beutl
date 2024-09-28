using System.ComponentModel;
using Beutl.Animation;

namespace Beutl.Graphics.Effects;

public sealed class CombinedFilterEffect : FilterEffect
{
    public static readonly CoreProperty<FilterEffect?> FirstProperty;
    public static readonly CoreProperty<FilterEffect?> SecondProperty;
    private FilterEffect? _first;
    private FilterEffect? _second;

    static CombinedFilterEffect()
    {
        FirstProperty = ConfigureProperty<FilterEffect?, CombinedFilterEffect>(nameof(First))
            .Accessor(o => o.First, (o, v) => o.First = v)
            .Register();

        SecondProperty = ConfigureProperty<FilterEffect?, CombinedFilterEffect>(nameof(Second))
            .Accessor(o => o.Second, (o, v) => o.Second = v)
            .Register();

        AffectsRender<CombinedFilterEffect>(FirstProperty, SecondProperty);
        Hierarchy<CombinedFilterEffect>(FirstProperty, SecondProperty);
    }

    public CombinedFilterEffect()
    {
    }

    public CombinedFilterEffect(FilterEffect? first, FilterEffect? second)
    {
        First = first;
        Second = second;
    }

    public FilterEffect? First
    {
        get => _first;
        set => SetAndRaise(FirstProperty, ref _first, value);
    }

    public FilterEffect? Second
    {
        get => _second;
        set => SetAndRaise(SecondProperty, ref _second, value);
    }

    public override Rect TransformBounds(Rect bounds)
    {
        return (First, Second) switch
        {
            (null or { IsEnabled: false }, { IsEnabled: true }) => Second.TransformBounds(bounds),
            ({ IsEnabled: true }, null or { IsEnabled: false }) => First.TransformBounds(bounds),
            ({ IsEnabled: true }, { IsEnabled: true }) => Second.TransformBounds(First.TransformBounds(bounds)),
            _ => bounds,
        };
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (First as IAnimatable)?.ApplyAnimations(clock);
        (Second as IAnimatable)?.ApplyAnimations(clock);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.Apply(First);
        context.Apply(Second);
    }
}
