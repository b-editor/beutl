using Beutl.Graphics.Rendering;
using Beutl.Media;
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

internal interface IFEItem_Skia : IFEItem
{
    void Accepts(FilterEffectActivator activator, SKImageFilterBuilder builder);

    bool SupportsDirectReplay { get; }

    void AcceptsDirect(SKImageFilterBuilder builder);

    /// <summary>
    /// When true, the bounds mapping is resolved from the combined execution-time target
    /// bounds instead of per-target authoring-time bounds, and the item is an
    /// <see cref="IFEItem_DeferredBounds"/>. Recording-time bounds walks must treat such an item as
    /// symbolic rather than call <see cref="IFEItem.TransformBounds"/> on it.
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

/// <summary>
/// A Skia item recorded against symbolic input bounds: only the target bounds of the activation that runs
/// it can fix its mapping.
/// </summary>
internal interface IFEItem_DeferredBounds : IFEItem_Skia
{
    /// <summary>
    /// Returns this item with its mapping fixed from <paramref name="targetBounds"/>, the combined
    /// execution-time target bounds of one activation.
    /// </summary>
    /// <remarks>
    /// The resolution is handed back rather than stored here. One recorded item is shared by every
    /// activation of the context that holds it and of every shallow clone of that context, so a
    /// resolution kept on the item would report the first activation's mapping for all of them.
    /// </remarks>
    IFEItem_Skia ResolveForActivation(Rect targetBounds);
}

/// <summary>
/// A matrix filter whose matrix is resolved from the combined execution-time target bounds, because its
/// origin depends on input bounds a preceding custom effect may only re-target at execution time.
/// </summary>
internal sealed record FEItem_SkiaDeferredMatrix<T>(
    T Data,
    Func<T, Rect, Matrix> MatrixFactory,
    BitmapInterpolationMode InterpolationMode) : IFEItem_DeferredBounds
{
    public bool ResolveBoundsAtExecutionTime => true;

    public bool SupportsDirectReplay => false;

    // Unresolved, the mapping is unknown; a recording-time bounds walk that took a concrete answer here
    // would freeze a matrix built from provisional bounds.
    Rect IFEItem.TransformBounds(Rect bounds) => Rect.Invalid;

    public bool TryTransformSamplingBounds(Rect output, out Rect input)
    {
        // No sampling footprint: the resampling apron is a device-pixel quantity, and the density the
        // segment finally runs at is unknown here, so no logical margin can bound it.
        input = default;
        return false;
    }

    public IFEItem_Skia ResolveForActivation(Rect targetBounds)
    {
        Matrix matrix = MatrixFactory(Data, targetBounds);
        return new FEItem_Skia<(Matrix Matrix, BitmapInterpolationMode InterpolationMode)>(
            (matrix, InterpolationMode),
            static (d, input, _) => SKImageFilter.CreateMatrix(
                d.Matrix.ToSKMatrix(), d.InterpolationMode.ToSKSamplingOptions(), input),
            static (d, rect) => rect.IsInvalid ? Rect.Invalid : rect.TransformToAABB(d.Matrix));
    }

    public void Accepts(FilterEffectActivator activator, SKImageFilterBuilder builder)
        => throw new InvalidOperationException(
            "A deferred-bound item runs only through the resolution of one activation.");

    public void AcceptsDirect(SKImageFilterBuilder builder)
        => throw new InvalidOperationException("A deferred-bound matrix item has no direct-replay factory.");
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
