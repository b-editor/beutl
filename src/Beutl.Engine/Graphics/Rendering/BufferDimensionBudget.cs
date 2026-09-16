using Beutl.Graphics.Backend;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// The largest device extent an allocation held to this budget may have, on either axis.
/// </summary>
/// <remarks>
/// Every buffer-dimension question is asked of one of these: whether an extent may be handed to an
/// allocator at all (<see cref="Fits"/>), and how far a density has to come down for one to fit
/// (<see cref="ClampWorkingScale"/>). Which budget applies is decided once, where the caller knows what it
/// is doing, and then passed - planning keeps <see cref="EngineCeiling"/> so a plan means the same thing on
/// every device, while an allocation site takes <see cref="Resolve"/> with
/// <see cref="BufferBudgetScope.Allocation"/> so the buffer it asks for is one the device can attach.
/// </remarks>
public readonly record struct BufferDimensionBudget
{
    /// <summary>The engine's own ceiling on a buffer's device extent, before any device's is applied.</summary>
    private const int EngineCeilingDimension = 16384;

    /// <summary>The pixels a raster apron adds to each axis, one per side.</summary>
    private const int RasterApronPixels = 2;

    private BufferDimensionBudget(int maxDimension) => MaxDimension = maxDimension;

    /// <summary>
    /// Gets the largest device extent an allocation may have on either axis, or zero for a
    /// <see langword="default"/> budget, which names nothing and cannot be measured against.
    /// </summary>
    public int MaxDimension { get; }

    /// <summary>
    /// Gets the engine's own ceiling, device-independent, which is what planning measures against.
    /// </summary>
    /// <remarks>
    /// A plan clamped to whichever device compiled it would mean something else on the next one, so a plan
    /// is expressed against this and an allocation site whose density is its own re-clamps to
    /// <see cref="Resolve"/>. What a device can actually attach is <see cref="ForDevice"/>, which is smaller
    /// on some, and a buffer this large is not allocatable there.
    /// </remarks>
    public static BufferDimensionBudget EngineCeiling { get; } = new(EngineCeilingDimension);

    /// <summary>
    /// Gets the budget <paramref name="context"/> imposes: the engine's ceiling, or the device's own limit
    /// when that is smaller.
    /// </summary>
    /// <param name="context">
    /// The context whose device limit applies, or <see langword="null"/> when there is no device, in which
    /// case the ceiling stands in for one.
    /// </param>
    /// <remarks>
    /// A device's limit does not change while its context lives, so it is read once per context - but the
    /// shared context is replaceable (<see cref="GraphicsContextFactory.Shutdown"/> is public), and
    /// answering for the next device out of the last one's memo asks a device that attaches less for an
    /// attachment it cannot make. The memo therefore lives with the contexts it describes, keyed to the one
    /// that answered; see <see cref="GraphicsContextFactory"/>.
    /// </remarks>
    public static BufferDimensionBudget ForDevice(IGraphicsContext? context)
        => GraphicsContextFactory.ResolveAttachmentDimension(context) is { } deviceLimit
            ? new BufferDimensionBudget(Math.Min(EngineCeilingDimension, deviceLimit))
            : EngineCeiling;

    /// <summary>Gets a budget that names <paramref name="maxDimension"/> rather than resolving one.</summary>
    /// <remarks>
    /// Naming one lets a caller bound an allocator of its own, and lets a test pin a limit below every
    /// device it runs on so a refusal is observable without depending on the machine's GPU. It is not held
    /// to <see cref="EngineCeiling"/>: a caller that names a budget is speaking for its own allocator.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDimension"/> is not positive.</exception>
    public static BufferDimensionBudget Named(int maxDimension)
    {
        if (maxDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDimension), maxDimension, "The maximum buffer dimension must be positive.");
        }

        return new BufferDimensionBudget(maxDimension);
    }

    /// <summary>Gets the budget the device imposes on the allocation <paramref name="scope"/> names.</summary>
    /// <remarks>
    /// Resolved per call rather than held: a caller can outlive the moment the graphics context first
    /// answers, and until it does the engine ceiling stands in for the device's own limit.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scope"/> is not a defined scope.</exception>
    public static BufferDimensionBudget Resolve(BufferBudgetScope scope)
        => scope switch
        {
            BufferBudgetScope.Allocation => ForDevice(RenderTarget.ResolveCreationContextForAllocation()),
            BufferBudgetScope.Prediction => ForDevice(GraphicsContextFactory.SharedContext),
            _ => throw new ArgumentOutOfRangeException(
                nameof(scope), scope, "The buffer budget scope is invalid."),
        };

    /// <summary>
    /// Refuses a budget that names nothing where one is accepted, rather than at its first use.
    /// </summary>
    /// <remarks>
    /// Only <see langword="default"/> reaches here - <see cref="Named"/> refuses a non-positive dimension and
    /// the resolvers cannot produce one - but a type that holds a budget for later should say so at the
    /// boundary it was handed one, the way it did when the budget was an <see cref="int"/>.
    /// </remarks>
    internal static void ThrowIfUninitialized(BufferDimensionBudget? budget, string paramName)
    {
        if (budget is { MaxDimension: <= 0 })
        {
            throw new ArgumentOutOfRangeException(
                paramName, budget, "The buffer budget must name a positive dimension.");
        }
    }

    /// <summary>Answers whether an allocation of <paramref name="deviceSize"/> fits this budget.</summary>
    /// <remarks>
    /// A caller that owns its density reduces it with <see cref="ClampWorkingScale"/> instead. This is for
    /// the ones that cannot - a pool sees pixels rather than a density, and the executor has to keep the
    /// density its plan and its cache entries were keyed on.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This is a <see langword="default"/> budget.</exception>
    public bool Fits(PixelSize deviceSize)
    {
        ThrowIfUninitialized();
        return deviceSize.Width <= MaxDimension && deviceSize.Height <= MaxDimension;
    }

    /// <summary>
    /// Reduces <paramref name="workingScale"/> until the device footprint
    /// <see cref="PixelRect.FromRect(Rect, float)"/> would allocate for <paramref name="logicalBounds"/>
    /// fits this budget on both axes. The scale is never raised, and the logical extents alone are also kept
    /// within the budget so the result stays independent of where the caller finally places the buffer.
    /// </summary>
    /// <remarks>
    /// A site whose density is the plan's cannot re-clamp against a device budget: the render cache keys an
    /// entry on the planned materialization density and rejects a payload recorded at any other, so lowering
    /// the density there turns a cacheable fragment into a failed capture. Those sites clamp against
    /// <see cref="EngineCeiling"/> and let the allocation refuse instead - see <see cref="Fits"/>, which
    /// degrades a preview and fails a delivery render through the lease session's existing contract.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This is a <see langword="default"/> budget.</exception>
    public float ClampWorkingScale(Rect logicalBounds, float workingScale)
    {
        ThrowIfUninitialized();

        if (!float.IsFinite(workingScale) || workingScale <= 0f)
            return workingScale;

        return FitScaleToDeviceFootprint(logicalBounds, workingScale, MaxDimension, apronPixels: 0);
    }

    /// <summary>
    /// <see cref="ClampWorkingScale"/> that leaves a footprint already within the budget exactly as it is.
    /// </summary>
    internal float ClampWorkingScaleToExactFootprint(Rect logicalBounds, float workingScale)
        => ClampWorkingScaleToExactFootprintBudget(logicalBounds, workingScale, apronPixels: 0);

    /// <summary>
    /// <see cref="ClampWorkingScaleToExactFootprint"/> with room kept for the apron
    /// <see cref="RenderScaleUtilities.AddRasterApron"/> adds.
    /// </summary>
    internal float ClampWorkingScaleToRasterApron(Rect logicalBounds, float workingScale)
        => ClampWorkingScaleToExactFootprintBudget(logicalBounds, workingScale, RasterApronPixels);

    private float ClampWorkingScaleToExactFootprintBudget(
        Rect logicalBounds,
        float workingScale,
        int apronPixels)
    {
        ThrowIfUninitialized();

        if (!float.IsFinite(workingScale) || workingScale <= 0f)
            return workingScale;

        if (HasFiniteBounds(logicalBounds)
            && FitsDeviceFootprint(logicalBounds, workingScale, MaxDimension, apronPixels))
        {
            return workingScale;
        }

        return FitScaleToDeviceFootprint(logicalBounds, workingScale, MaxDimension, apronPixels);
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

    private void ThrowIfUninitialized()
    {
        if (MaxDimension <= 0)
        {
            throw new InvalidOperationException(
                "default(BufferDimensionBudget) names no budget; use EngineCeiling, ForDevice, Named or Resolve.");
        }
    }
}
