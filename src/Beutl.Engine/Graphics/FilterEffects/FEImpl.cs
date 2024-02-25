using System.Collections.Immutable;

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

[Obsolete]
internal record FEItem_Custom<T>(
    T Data, Action<T, FilterEffectCustomOperationContext> Action, Func<T, Rect, Rect> TransformBounds)
    : FEItem<T>(Data, TransformBounds), IFEItem_Custom
{
    public void Accepts(CustomFilterEffectContext context)
    {
        context.ForEach((_, target) =>
        {
            using (target)
            {
                var innerContext = new FilterEffectCustomOperationContext(context._factory, target, [.. target._history]);
                Action.Invoke(Data, innerContext);

                innerContext.Target.Bounds = TransformBounds!(Data, innerContext.Target.Bounds);
                return innerContext.Target;
            }
        });
    }
}

internal record FEItem_CustomEffect<T>(
    T Data, Action<T, CustomFilterEffectContext> Action, Func<T, Rect, Rect>? TransformBounds)
    : FEItem<T>(Data, TransformBounds), IFEItem_Custom
{
    public void Accepts(CustomFilterEffectContext context)
    {
        Action.Invoke(Data, context);
    }
}

internal interface IFEItem_Custom
{
    void Accepts(CustomFilterEffectContext context);
}
