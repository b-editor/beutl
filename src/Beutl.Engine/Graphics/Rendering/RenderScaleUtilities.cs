using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Pure working-density calculations shared by recording, planning, 3D, brushes, and export policy.
/// </summary>
public static class RenderScaleUtilities
{
    public const int MaxBufferDimension = 16384;

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

    public static float ClampWorkingScaleToBufferBudget(
        Rect logicalBounds,
        float workingScale,
        int maxDimension = MaxBufferDimension)
    {
        if (maxDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDimension), maxDimension, "The maximum buffer dimension must be positive.");
        }

        if (!float.IsFinite(workingScale) || workingScale <= 0f)
            return workingScale;

        double maxAxis = Math.Max(
            Math.Abs((double)logicalBounds.Width),
            Math.Abs((double)logicalBounds.Height));
        if (!double.IsFinite(maxAxis) || maxAxis <= 0)
            return workingScale;

        double largestAxisPixels = Math.Ceiling(maxAxis * workingScale);
        if (largestAxisPixels <= maxDimension || largestAxisPixels <= 0)
            return workingScale;

        float fit = (float)(workingScale * (maxDimension / largestAxisPixels));
        while (fit > 0f && Math.Ceiling(maxAxis * fit) > maxDimension)
            fit = MathF.BitDecrement(fit);

        return MathF.Max(MathF.Min(workingScale, fit), 0f);
    }

    internal static float ClampWorkingScaleToExactBufferBudget(
        Rect logicalBounds,
        float workingScale,
        int maxDimension = MaxBufferDimension)
        => ClampWorkingScaleToExactFootprintBudget(
            logicalBounds,
            workingScale,
            maxDimension,
            includeRasterApron: false);

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
            includeRasterApron: true);

    private static float ClampWorkingScaleToExactFootprintBudget(
        Rect logicalBounds,
        float workingScale,
        int maxDimension,
        bool includeRasterApron)
    {
        if (maxDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDimension), maxDimension, "The maximum buffer dimension must be positive.");
        }

        if (!float.IsFinite(workingScale)
            || workingScale <= 0f
            || !HasFinitePositiveArea(logicalBounds))
        {
            return ClampWorkingScaleToBufferBudget(logicalBounds, workingScale, maxDimension);
        }

        if (FitsExactFootprint(logicalBounds, workingScale, maxDimension, includeRasterApron))
            return workingScale;

        float fit = ClampWorkingScaleToBufferBudget(logicalBounds, workingScale, maxDimension);
        while (fit > 0f)
        {
            if (FitsExactFootprint(logicalBounds, fit, maxDimension, includeRasterApron))
                return fit;
            fit = MathF.BitDecrement(fit);
        }

        return fit;
    }

    private static bool FitsExactFootprint(
        Rect logicalBounds,
        float workingScale,
        int maxDimension,
        bool includeRasterApron)
    {
        PixelRect footprint = PixelRect.FromRect(logicalBounds, workingScale);
        if (includeRasterApron)
            footprint = AddRasterApron(footprint);

        return footprint.Width <= maxDimension && footprint.Height <= maxDimension;
    }

    private static bool HasFinitePositiveArea(Rect bounds)
        => !bounds.IsInvalid
            && float.IsFinite(bounds.X)
            && float.IsFinite(bounds.Y)
            && float.IsFinite(bounds.Width)
            && float.IsFinite(bounds.Height)
            && bounds.Width > 0
            && bounds.Height > 0;
}
