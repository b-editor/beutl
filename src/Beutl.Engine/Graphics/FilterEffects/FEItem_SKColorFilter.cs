using SkiaSharp;

namespace Beutl.Graphics.Effects;

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

    public bool SupportsDirectReplay => false;

    public void AcceptsDirect(SKImageFilterBuilder builder)
        => throw new InvalidOperationException("This color filter has no direct-replay factory.");
}
