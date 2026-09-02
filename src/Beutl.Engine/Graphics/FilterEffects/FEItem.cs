namespace Beutl.Graphics.Effects;

internal abstract record FEItem<T>(T Data, Func<T, Rect, Rect>? TransformBounds) : IFEItem
{
    Rect IFEItem.TransformBounds(Rect bounds)
    {
        return TransformBounds?.Invoke(Data, bounds) ?? Rect.Invalid;
    }
}
