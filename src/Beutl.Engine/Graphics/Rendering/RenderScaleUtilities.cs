using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Pure working-density calculations shared by recording, planning, 3D, brushes, and export policy.
/// </summary>
public static class RenderScaleUtilities
{
    /// <summary>
    /// The engine's own ceiling on a buffer's device extent, before the device's is taken into account.
    /// </summary>
    /// <remarks>
    /// Planning uses this so a plan means the same thing on every device. What a device can actually attach
    /// is <see cref="ResolveMaxBufferDimension"/>, which is smaller on some, and a buffer this large is not
    /// allocatable there - see the note on that method.
    /// </remarks>
    public const int MaxBufferDimension = 16384;

    private static int s_resolvedMaxBufferDimension;

    /// <summary>
    /// The largest device extent a buffer may have here: the engine's ceiling, or the device's own limit
    /// when that is smaller.
    /// </summary>
    /// <remarks>
    /// An intermediate is drawn into and then sampled, so it has to satisfy the device's framebuffer limit
    /// as well as its image limit, and a device may report either below the engine's ceiling. Clamping to a
    /// fixed number instead asks such a device for an attachment it cannot make, which it reports as
    /// undefined behaviour rather than a failed allocation. The device's limit does not change while a
    /// context lives, so it is read once.
    /// </remarks>
    public static int ResolveMaxBufferDimension()
    {
        int resolved = Volatile.Read(ref s_resolvedMaxBufferDimension);
        if (resolved > 0)
            return resolved;

        int deviceLimit = Backend.GraphicsContextFactory.SharedContext?.MaxAttachmentDimension ?? 0;
        resolved = deviceLimit > 0 ? Math.Min(MaxBufferDimension, deviceLimit) : MaxBufferDimension;

        // Only remember it once a context has actually answered; before that the ceiling is a placeholder
        // and the next caller should ask again.
        if (deviceLimit > 0)
            Volatile.Write(ref s_resolvedMaxBufferDimension, resolved);

        return resolved;
    }

    private const int RasterApronPixels = 2;

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
        if (!float.IsFinite(outputScale) || outputScale <= 0f)
            outputScale = 1f;

        float supply = outputScale;
        foreach (EffectiveScale input in inputs)
        {
            if (!input.IsUnbounded && input.Value > supply)
                supply = input.Value;
        }

        return MathF.Min(supply, SanitizeMaxWorkingScale(maxWorkingScale));
    }

    /// <summary>
    /// Reduces <paramref name="workingScale"/> until the device footprint
    /// <see cref="PixelRect.FromRect(Rect, float)"/> would allocate for <paramref name="logicalBounds"/>
    /// fits <paramref name="maxDimension"/> on both axes. The scale is never raised, and the logical
    /// extents alone are also kept within the budget so the result stays independent of where the
    /// caller finally places the buffer.
    /// </summary>
    public static float ClampWorkingScaleToBufferBudget(
        Rect logicalBounds,
        float workingScale,
        int maxDimension = MaxBufferDimension)
    {
        int budget = maxDimension;
        ValidateMaxDimension(budget);

        if (!float.IsFinite(workingScale) || workingScale <= 0f)
            return workingScale;

        return FitScaleToDeviceFootprint(logicalBounds, workingScale, budget, apronPixels: 0);
    }

    /// <summary>
    /// <see cref="ClampWorkingScaleToBufferBudget"/> against what the device can actually attach.
    /// </summary>
    /// <param name="logicalBounds">The logical extents the buffer has to cover.</param>
    /// <param name="workingScale">The density the caller would allocate at.</param>
    /// <param name="maxDimension">
    /// The budget to fit, or <see langword="null"/> for <see cref="ResolveMaxBufferDimension"/>.
    /// </param>
    /// <remarks>
    /// This belongs to allocation, not planning: a plan clamped to whichever device compiled it would mean
    /// something else on the next one, so planning keeps <see cref="MaxBufferDimension"/> and the site that
    /// turns a density into real pixels re-clamps here. That site reports the density it allocated at, so
    /// the buffer and the density read back from it stay the same number.
    /// </remarks>
    public static float ClampWorkingScaleToDeviceBufferBudget(
        Rect logicalBounds,
        float workingScale,
        int? maxDimension = null)
        => ClampWorkingScaleToBufferBudget(
            logicalBounds,
            workingScale,
            maxDimension ?? ResolveMaxBufferDimension());

    internal static float ClampWorkingScaleToExactBufferBudget(
        Rect logicalBounds,
        float workingScale,
        int maxDimension = MaxBufferDimension)
        => ClampWorkingScaleToExactFootprintBudget(
            logicalBounds,
            workingScale,
            maxDimension,
            apronPixels: 0);

    /// <inheritdoc cref="ClampWorkingScaleToDeviceBufferBudget"/>
    internal static float ClampWorkingScaleToExactDeviceBufferBudget(
        Rect logicalBounds,
        float workingScale,
        int? maxDimension = null)
        => ClampWorkingScaleToExactBufferBudget(
            logicalBounds,
            workingScale,
            maxDimension ?? ResolveMaxBufferDimension());

    internal static PixelRect AddRasterApron(PixelRect bounds)
        => new(
            checked(bounds.X - 1),
            checked(bounds.Y - 1),
            checked(bounds.Width + 2),
            checked(bounds.Height + 2));

    internal static float ClampWorkingScaleToRasterApronBudget(
        Rect logicalBounds,
        float workingScale,
        int maxDimension = MaxBufferDimension)
        => ClampWorkingScaleToExactFootprintBudget(
            logicalBounds,
            workingScale,
            maxDimension,
            RasterApronPixels);

    private static float ClampWorkingScaleToExactFootprintBudget(
        Rect logicalBounds,
        float workingScale,
        int maxDimension,
        int apronPixels)
    {
        ValidateMaxDimension(maxDimension);

        if (!float.IsFinite(workingScale) || workingScale <= 0f)
            return workingScale;

        if (HasFiniteBounds(logicalBounds)
            && FitsDeviceFootprint(logicalBounds, workingScale, maxDimension, apronPixels))
        {
            return workingScale;
        }

        return FitScaleToDeviceFootprint(logicalBounds, workingScale, maxDimension, apronPixels);
    }

    private static float FitScaleToDeviceFootprint(
        Rect logicalBounds,
        float workingScale,
        int maxDimension,
        int apronPixels)
    {
        double maxAxis = MaxLogicalAxis(logicalBounds);

        // A fractional origin can push the footprint one device pixel past ceil(extent * scale), so the
        // extent estimate is only a seed: give a pixel back until the footprint itself fits.
        for (int budget = maxDimension - apronPixels; budget > 0; budget--)
        {
            float candidate = FitScaleToLogicalExtent(maxAxis, workingScale, budget);
            if (candidate <= 0f)
                break;

            if (FitsDeviceFootprint(logicalBounds, candidate, maxDimension, apronPixels))
                return candidate;

            // Without a finite positive extent, a lower scale cannot shrink the footprint any further.
            if (!double.IsFinite(maxAxis) || maxAxis <= 0)
                return workingScale;
        }

        // No candidate footprint fit, which a degenerate rectangle can produce at every scale. Zero is not a
        // density any caller can use - the working-scale policy rejects it - so a clamp that cannot clamp
        // hands back what it was given. An unallocatable buffer is then reported by the allocation itself,
        // which already degrades a preview and fails a delivery render.
        return workingScale;
    }

    private static float FitScaleToLogicalExtent(double maxAxis, float workingScale, int budget)
    {
        if (!double.IsFinite(maxAxis) || maxAxis <= 0)
            return workingScale;

        double largestAxisPixels = Math.Ceiling(maxAxis * workingScale);
        if (largestAxisPixels <= budget || largestAxisPixels <= 0)
            return workingScale;

        float fit = (float)(workingScale * (budget / largestAxisPixels));
        while (fit > 0f && Math.Ceiling(maxAxis * fit) > budget)
            fit = MathF.BitDecrement(fit);

        return MathF.Max(MathF.Min(workingScale, fit), 0f);
    }

    private static bool FitsDeviceFootprint(
        Rect logicalBounds,
        float workingScale,
        int maxDimension,
        int apronPixels)
    {
        int budget = maxDimension - apronPixels;
        double left = Math.Floor((double)logicalBounds.Left * workingScale);
        double top = Math.Floor((double)logicalBounds.Top * workingScale);
        double right = Math.Ceiling((double)logicalBounds.Right * workingScale);
        double bottom = Math.Ceiling((double)logicalBounds.Bottom * workingScale);
        double width = right - left;
        double height = bottom - top;

        return double.IsFinite(width)
            && double.IsFinite(height)
            && width >= 0
            && height >= 0
            && width <= budget
            && height <= budget;
    }

    private static double MaxLogicalAxis(Rect bounds)
        => Math.Max(Math.Abs((double)bounds.Width), Math.Abs((double)bounds.Height));

    private static bool HasFiniteBounds(Rect bounds)
        => !bounds.IsInvalid
            && float.IsFinite(bounds.X)
            && float.IsFinite(bounds.Y)
            && float.IsFinite(bounds.Width)
            && float.IsFinite(bounds.Height);

    private static void ValidateMaxDimension(int maxDimension)
    {
        if (maxDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDimension), maxDimension, "The maximum buffer dimension must be positive.");
        }
    }
}
