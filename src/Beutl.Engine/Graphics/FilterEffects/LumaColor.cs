namespace Beutl.Graphics.Effects;

public sealed class LumaColor : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context)
    {
        context.LumaColor();
    }
}
