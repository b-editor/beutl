using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Pure working-density calculations shared by recording, planning, 3D, brushes, and export policy.
/// </summary>
public static class RenderScaleUtilities
{
    public const int MaxBufferDimension = 16384;

    private const int RasterApronPixels = 2;

    public static float SanitizeMaxWorkingScale(float maxWorkingScale)
        => float.IsNaN(maxWorkingScale) || maxWorkingScale <= 0f
            ? float.PositiveInfinity
            : maxWorkingScale;

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
        ValidateMaxDimension(maxDimension);

        if (!float.IsFinite(workingScale) || workingScale <= 0f)
            return workingScale;

        return FitScaleToDeviceFootprint(logicalBounds, workingScale, maxDimension, apronPixels: 0);
    }

    internal static float ClampWorkingScaleToExactBufferBudget(
        Rect logicalBounds,
        float workingScale,
        int maxDimension = MaxBufferDimension)
        => ClampWorkingScaleToExactFootprintBudget(
            logicalBounds,
            workingScale,
            maxDimension,
            apronPixels: 0);

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

        return 0f;
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
