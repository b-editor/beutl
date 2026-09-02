using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal record FEItem_Skia<T>(
    T Data, Func<T, SKImageFilter?, FilterEffectActivator, SKImageFilter?> Factory, Func<T, Rect, Rect> TransformBounds)
    : FEItem<T>(Data, TransformBounds), IFEItem_Skia
{
    public Func<T, SKImageFilter?, SKImageFilter?>? DirectFactory { get; init; }

    /// <summary>
    /// Always <see langword="false"/>: this item's mapping is fixed at construction. Deferral is
    /// <see cref="IFEItem_DeferredBounds"/>, which hands each activation its own resolution instead of
    /// letting one recorded item carry the first activation's.
    /// </summary>
    public bool ResolveBoundsAtExecutionTime => false;

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

    public bool SupportsDirectReplay => DirectFactory is not null;

    public void AcceptsDirect(SKImageFilterBuilder builder)
    {
        builder.AppendSkiaFilter(Data, DirectFactory!);
    }
}
