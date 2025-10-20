namespace Beutl.Graphics.Effects;

public sealed partial class LumaColor : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        context.LumaColor();
    }
}
