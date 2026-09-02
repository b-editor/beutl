using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

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
