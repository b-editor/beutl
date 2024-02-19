using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal interface IFEItem
{
    Rect TransformBounds(Rect bounds);
}

internal abstract record FEItem<T>(T Data, Func<T, Rect, Rect>? TransformBounds) : IFEItem
{
    Rect IFEItem.TransformBounds(Rect bounds)
    {
        return TransformBounds?.Invoke(Data, bounds) ?? Rect.Invalid;
    }
}

internal record FEItem_Skia<T>(
    T Data, Func<T, SKImageFilter?, FilterEffectActivator, SKImageFilter?> Factory, Func<T, Rect, Rect> TransformBounds)
    : FEItem<T>(Data, TransformBounds), IFEItem_Skia
{
    public void Accepts(FilterEffectActivator activator, SKImageFilterBuilder builder)
    {
        builder.AppendSkiaFilter(Data, activator, Factory);
    }
}

internal record FEItem_SKColorFilter<T>(
    T Data, Func<T, FilterEffectActivator, SKColorFilter?> Factory)
    : FEItem<T>(Data, (_, rect) => rect), IFEItem_Skia
{
    public void Accepts(FilterEffectActivator activator, SKImageFilterBuilder builder)
    {
        builder.AppendSKColorFilter(Data, activator, Factory);
    }
}

internal interface IFEItem_Skia
{
    void Accepts(FilterEffectActivator activator, SKImageFilterBuilder builder);
}

internal record FEItem_Custom<T>(
    T Data, Action<T, FilterEffectCustomOperationContext> Action, Func<T, Rect, Rect>? TransformBounds)
    : FEItem<T>(Data, TransformBounds), IFEItem_Custom
{
    public void Accepts(FilterEffectCustomOperationContext context)
    {
        Action.Invoke(Data, context);
    }
}

internal interface IFEItem_Custom
{
    void Accepts(FilterEffectCustomOperationContext context);
}
