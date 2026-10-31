namespace Beutl.Graphics.Effects;

internal interface IFEItem_Custom
{
    CustomFilterEffectOptions Options { get; }

    void Accepts(CustomFilterEffectContext context);
}
