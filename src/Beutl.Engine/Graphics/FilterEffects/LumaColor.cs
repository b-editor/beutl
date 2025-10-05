namespace Beutl.Graphics.Effects;

public sealed partial class LumaColor : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context)
    {
        context.LumaColor();
    }
}
