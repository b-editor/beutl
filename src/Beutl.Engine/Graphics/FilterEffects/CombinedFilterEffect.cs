using Beutl.Animation;
using Beutl.Engine;

namespace Beutl.Graphics.Effects;

[Obsolete("Use FilterEffectGroup instead.")]
public sealed partial class CombinedFilterEffect : FilterEffect
{
    public CombinedFilterEffect()
    {
        ScanProperties<CombinedFilterEffect>();
    }

    public CombinedFilterEffect(FilterEffect? first, FilterEffect? second)
        : this()
    {
        First.CurrentValue = first;
        Second.CurrentValue = second;
    }

    public IProperty<FilterEffect?> First { get; } = Property.Create<FilterEffect?>();

    public IProperty<FilterEffect?> Second { get; } = Property.Create<FilterEffect?>();

    public override void ApplyTo(FilterEffectContext context)
    {
        context.Apply(First.CurrentValue);
        context.Apply(Second.CurrentValue);
    }
}
