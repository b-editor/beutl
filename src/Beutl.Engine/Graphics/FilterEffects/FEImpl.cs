using Beutl.Graphics.Rendering;
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
    /// <summary>
    /// Resolves <see cref="IFEItem.TransformBounds"/> from the combined execution-time target
    /// bounds when authoring-time bounds are unavailable (symbolic input).
    /// </summary>
    public bool ResolveBoundsAtExecutionTime { get; init; }

    /// <summary>
    /// Maps a requested output region to the input region the built <see cref="SKImageFilter"/> reads, or
    /// <see langword="null"/> when the footprint is not proven.
    /// </summary>
    public Func<T, Rect, Rect>? TransformSamplingBounds { get; init; }

    public bool TryTransformSamplingBounds(Rect output, out Rect input)
    {
        if (TransformSamplingBounds is null)
        {
            input = default;
            return false;
        }

        input = TransformSamplingBounds(Data, output);
        return true;
    }

    public void Accepts(FilterEffectActivator activator, SKImageFilterBuilder builder)
    {
        builder.AppendSkiaFilter(Data, activator, Factory);
    }
}

internal record FEItem_SKColorFilter<T>(
    T Data, Func<T, FilterEffectActivator, SKColorFilter?> Factory)
    : FEItem<T>(Data, (_, rect) => rect), IFEItem_Skia
{
    public bool ResolveBoundsAtExecutionTime => false;

    public bool TryTransformSamplingBounds(Rect output, out Rect input)
    {
        // A color filter is evaluated per pixel, so it never reads outside the requested region.
        input = output;
        return true;
    }

    public void Accepts(FilterEffectActivator activator, SKImageFilterBuilder builder)
    {
        builder.AppendSKColorFilter(Data, activator, Factory);
    }
}

internal interface IFEItem_Skia
{
    void Accepts(FilterEffectActivator activator, SKImageFilterBuilder builder);

    /// <summary>
    /// When true, the bounds mapping is resolved from the combined execution-time target
    /// bounds instead of per-target authoring-time bounds.
    /// </summary>
    bool ResolveBoundsAtExecutionTime { get; }

    /// <summary>
    /// Maps a requested output region to the input region this item reads while producing it.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the item declares no proven sampling footprint; the caller must then
    /// require the complete input. A footprint is never inferred from <see cref="IFEItem.TransformBounds"/>,
    /// which may legitimately be narrower than what the filter reads.
    /// </returns>
    bool TryTransformSamplingBounds(Rect output, out Rect input);
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

internal sealed record FEItem_Shader(ShaderDescription Description) : IFEItem
{
    public Rect TransformBounds(Rect bounds) => Description.Bounds.TransformBounds(bounds);
}

internal sealed record FEItem_Geometry(GeometryDescription Description) : IFEItem
{
    public Rect TransformBounds(Rect bounds) => Description.Bounds.TransformBounds(bounds);
}
