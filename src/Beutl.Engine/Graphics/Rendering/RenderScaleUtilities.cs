using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Pure working-density calculations shared by recording, planning, 3D, brushes, and export policy.
/// </summary>
/// <remarks>
/// How large a buffer may be is not asked here: that is a device-dependent budget a caller resolves once
/// and passes, and it lives on <see cref="BufferDimensionBudget"/> together with the clamps that measure
/// against it.
/// </remarks>
public static class RenderScaleUtilities
{
    /// <summary>Answers whether a logical rectangle can be asked of a buffer allocator at all.</summary>
    /// <remarks>
    /// Prior to <see cref="BufferDimensionBudget.Fits"/>, which asks whether a device size is within the
    /// axis limit. A non-finite or non-positive extent is not a size the allocator can be given a budget
    /// for: it reaches the native allocator as a nonsense request rather than as a failed one.
    /// </remarks>
    internal static bool IsAllocatableBounds(Rect bounds)
        => double.IsFinite(bounds.X)
           && double.IsFinite(bounds.Y)
           && double.IsFinite(bounds.Width)
           && double.IsFinite(bounds.Height)
           && bounds.Width > 0
           && bounds.Height > 0;

    /// <summary>Answers whether a logical rectangle is renderable but covers nothing.</summary>
    /// <remarks>
    /// A finite, non-negative rectangle with a zero extent on either axis. Distinct from the bounds
    /// <see cref="IsAllocatableBounds"/> rejects: those are an allocation failure a caller reports, while
    /// this is a target with nothing to draw, which a caller drops in every render intent.
    /// </remarks>
    internal static bool IsEmptyBounds(Rect bounds)
        => double.IsFinite(bounds.Width)
           && double.IsFinite(bounds.Height)
           && bounds.Width >= 0
           && bounds.Height >= 0
           && (bounds.Width == 0 || bounds.Height == 0);

    /// <summary>
    /// The output density <c>s_out</c> a caller asked for, or 1 when that is not a density.
    /// </summary>
    /// <remarks>
    /// A recording context and the request that executes what it recorded have to agree on this number:
    /// a drawable may size or branch its recorded graph on the density it is given, so a graph recorded
    /// for one density and executed at another is incoherent. Both sides normalize here so neither can
    /// be given a value the other rejects.
    /// </remarks>
    public static float SanitizeOutputScale(float outputScale)
        => float.IsFinite(outputScale) && outputScale > 0f ? outputScale : 1f;

    public static float SanitizeMaxWorkingScale(float maxWorkingScale)
        => float.IsNaN(maxWorkingScale) || maxWorkingScale <= 0f
            ? float.PositiveInfinity
            : maxWorkingScale;

    internal static bool IsExactIntegerReduction(float scale)
    {
        if (!float.IsFinite(scale) || scale <= 0f || scale >= 1f)
            return false;

        float reduction = 1f / scale;
        return MathF.Abs(reduction - MathF.Round(reduction)) <= 0.0001f;
    }

    public static float ResolveWorkingScale(
        ReadOnlySpan<EffectiveScale> inputs,
        float outputScale,
        float maxWorkingScale = float.PositiveInfinity)
    {
        outputScale = SanitizeOutputScale(outputScale);

        float supply = outputScale;
        foreach (EffectiveScale input in inputs)
        {
            if (!input.IsUnbounded && input.Value > supply)
                supply = input.Value;
        }

        return MathF.Min(supply, SanitizeMaxWorkingScale(maxWorkingScale));
    }

    internal static PixelRect AddRasterApron(PixelRect bounds)
        => new(
            checked(bounds.X - 1),
            checked(bounds.Y - 1),
            checked(bounds.Width + 2),
            checked(bounds.Height + 2));
}
