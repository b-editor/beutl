using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class RenderNodeRasterization : IDisposable
{
    private Bitmap? _bitmap;

    internal RenderNodeRasterization(Rect bounds, float outputScale, Bitmap? bitmap)
    {
        if (!RenderRectValidation.IsFiniteNonNegative(bounds))
        {
            throw new ArgumentException(
                "Rasterization bounds must be finite and have non-negative dimensions.",
                nameof(bounds));
        }

        if (!float.IsFinite(outputScale) || outputScale <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputScale),
                outputScale,
                "Rasterization output scale must be positive and finite.");
        }

        bool empty = bounds.Width == 0 || bounds.Height == 0;
        if (empty != (bitmap is null))
        {
            throw new ArgumentException(
                "An empty rasterization has no bitmap, while a non-empty rasterization requires one.",
                nameof(bitmap));
        }

        Bounds = bounds;
        OutputScale = outputScale;
        _bitmap = bitmap;
    }

    public Rect Bounds { get; }

    public float OutputScale { get; }

    public Bitmap? Bitmap
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _bitmap;
        }
    }

    public bool IsEmpty => Bounds.Width == 0 || Bounds.Height == 0;

    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        Bitmap? bitmap = Interlocked.Exchange(ref _bitmap, null);
        bitmap?.Dispose();
    }
}

public readonly record struct RenderNodeMeasurement(
    Rect OutputBounds,
    Rect QueryBounds,
    EffectiveScale EffectiveScale,
    RenderValueCardinality ValueCardinality,
    bool HasFragments,
    bool HasContributingValues,
    bool HasTargetEffects);
