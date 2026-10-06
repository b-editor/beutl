namespace Beutl.Graphics.Effects;

internal record FEItem_CustomEffect<T>(
    T Data, Action<T, CustomFilterEffectContext> Action, Func<T, Rect, Rect>? TransformBounds)
    : FEItem<T>(Data, TransformBounds), IFEItem_Custom
{
    public void Accepts(CustomFilterEffectContext context)
    {
        Action.Invoke(Data, context);
    }
}
