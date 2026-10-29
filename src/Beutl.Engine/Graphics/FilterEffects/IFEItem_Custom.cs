namespace Beutl.Graphics.Effects;

internal interface IFEItem_Custom
{
    CustomFilterEffectOptions Options => CustomFilterEffectOptions.None;

    void Accepts(CustomFilterEffectContext context);
}
