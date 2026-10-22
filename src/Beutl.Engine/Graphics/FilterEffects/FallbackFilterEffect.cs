using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

public sealed partial class FallbackFilterEffect : FilterEffect, IFallback
{
    // An effect whose type did not load leaves what it is applied to as it is.
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
    }
}
