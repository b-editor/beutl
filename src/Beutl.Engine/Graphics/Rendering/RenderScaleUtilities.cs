using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Pure working-density and buffer-allocatability calculations shared by recording, planning, 3D, brushes,
/// and export policy.
/// </summary>
public static class RenderScaleUtilities
{
    /// <summary>
    /// The engine's own ceiling on a buffer's device extent, before the device's is taken into account.
    /// </summary>
    /// <remarks>
    /// Planning uses this so a plan means the same thing on every device. What a device can actually attach
    /// is <see cref="ResolveMaxBufferDimension()"/>, which is smaller on some, and a buffer this large is not
    /// allocatable there - see the note on that method. A caller off the render thread that is predicting
    /// rather than allocating reads <see cref="PredictRenderThreadMaxBufferDimension"/> instead.
    /// </remarks>
    public const int MaxBufferDimension = 16384;

    // The limit and the context that reported it live in one field, so a reader can never pair one
    // context's identity with another's answer.
    private sealed class ResolvedBufferDimension(Backend.IGraphicsContext context, int value)
    {
        public Backend.IGraphicsContext Context { get; } = context;

        public int Value { get; } = value;
    }

    private static ResolvedBufferDimension? s_resolvedMaxBufferDimension;

    /// <summary>
    /// The largest device extent a buffer may have here: the engine's ceiling, or the active shared
    /// context's own limit when that is smaller.
    /// </summary>
    /// <remarks>
    /// An intermediate is drawn into and then sampled, so it has to satisfy the device's framebuffer limit
    /// as well as its image limit, and a device may report either below the engine's ceiling. Clamping to a
    /// fixed number instead asks such a device for an attachment it cannot make, which it reports as
    /// undefined behaviour rather than a failed allocation. A device's limit does not change while its
    /// context lives, so it is read once per context - but the shared context is replaceable
    /// (<see cref="Backend.GraphicsContextFactory.Shutdown"/> is public), and answering for the next device
    /// out of the last one's memo is that same undefined behaviour whenever it can attach less. The memo is
    /// therefore keyed to the context that answered, so any other context re-reads however it was replaced.
    /// <para>
    /// A buffer allocated off the render thread never reaches that device at all -
    /// <see cref="RenderTarget.Create"/> rasters it on the CPU - so the shared context applies only where
    /// that allocation would attach to it.
    /// Where it does, the context is the one that allocation would build rather than whichever is installed
    /// now: before any GPU work there is none installed, and answering the engine ceiling there admits a
    /// buffer the device built moments later cannot attach.
    /// </para>
    /// </remarks>
    public static int ResolveMaxBufferDimension()
        => ResolveMaxBufferDimension(RenderTarget.ResolveCreationContextForAllocation());

    /// <summary>
    /// What <see cref="ResolveMaxBufferDimension()"/> is expected to answer on the render thread, asked from
    /// a thread that is not it.
    /// </summary>
    /// <remarks>
    /// Pre-validation is not allocation. A dialog that asks whether an export will fit is predicting the
    /// limit the render thread will resolve later, and <see cref="ResolveMaxBufferDimension()"/> cannot be
    /// that prediction: it answers for the caller's own allocation, which off the render thread
    /// <see cref="RenderTarget.Create"/> rasters on the CPU, so it correctly reports the engine ceiling
    /// there. Pre-validating against that clears a size the render then refuses once the user has already
    /// chosen a file.
    /// <para>
    /// A context reports a limit fixed for its lifetime, so reading an installed one from another thread is
    /// sound. What that thread cannot do is build one - <see cref="Backend.GraphicsContextFactory.GetOrCreateShared"/>
    /// is render-thread-only - so before any GPU work the answer is <see cref="MaxBufferDimension"/>. That is
    /// not a measurement standing in for one: the resolved limit is the smaller of the ceiling and the
    /// device, so the ceiling is the bound every answer satisfies, and it is only ever loose until the first
    /// frame is drawn. Erring open is also the right direction for a courtesy check - the allocation itself
    /// refuses authoritatively, so a warning not given costs a late failure, while one invented for a device
    /// that turns out to be larger would refuse an export that would have worked.
    /// </para>
    /// </remarks>
    public static int PredictRenderThreadMaxBufferDimension()
    {
        Backend.IGraphicsContext? installed = Backend.GraphicsContextFactory.SharedContext;
        return installed is null ? MaxBufferDimension : ResolveMaxBufferDimension(installed);
    }

    /// <summary>
    /// <see cref="ResolveMaxBufferDimension()"/> against a named context rather than the shared one.
    /// </summary>
    /// <param name="context">
    /// The context whose device limit applies, or <see langword="null"/> when there is none.
    /// </param>
    internal static int ResolveMaxBufferDimension(Backend.IGraphicsContext? context)
    {
        ResolvedBufferDimension? resolved = Volatile.Read(ref s_resolvedMaxBufferDimension);
        if (resolved is not null && ReferenceEquals(resolved.Context, context))
            return resolved.Value;

        if (context is null)
        {
            // Dropping the memo here stops it outliving the context it describes, and stops a disposed
            // context being kept alive by it.
            if (resolved is not null)
                Volatile.Write(ref s_resolvedMaxBufferDimension, null);

            return MaxBufferDimension;
        }

        // Only remember it once a context has actually answered; before that the ceiling is a placeholder
        // and the next caller should ask again.
        int deviceLimit = context.MaxAttachmentDimension;
        if (deviceLimit <= 0)
            return MaxBufferDimension;

        int value = Math.Min(MaxBufferDimension, deviceLimit);
        Volatile.Write(ref s_resolvedMaxBufferDimension, new ResolvedBufferDimension(context, value));
        return value;
    }

    /// <summary>
    /// Whether an allocation of <paramref name="deviceSize"/> fits what the device can attach.
    /// </summary>
    /// <param name="deviceSize">The device extents an allocation would ask for.</param>
    /// <param name="maxDimension">
    /// The budget to fit, or <see langword="null"/> for <see cref="ResolveMaxBufferDimension()"/>.
    /// </param>
    /// <remarks>
    /// A caller that owns its density reduces it with <see cref="ClampWorkingScaleToDeviceBufferBudget"/>
    /// instead. This is for the ones that cannot - a pool sees pixels rather than a density, and the executor
    /// has to keep the density its plan and its cache entries were keyed on.
    /// </remarks>
    public static bool FitsBufferBudget(PixelSize deviceSize, int? maxDimension = null)
    {
        int budget = maxDimension ?? ResolveMaxBufferDimension();
        ValidateMaxDimension(budget);
        return deviceSize.Width <= budget && deviceSize.Height <= budget;
    }

    /// <summary>Answers whether a logical rectangle can be asked of a buffer allocator at all.</summary>
    /// <remarks>
    /// Prior to <see cref="FitsBufferBudget"/>, which asks whether a device size is within the axis limit.
    /// A non-finite or non-positive extent is not a size the allocator can be given a budget for: it reaches
    /// the native allocator as a nonsense request rather than as a failed one.
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

    private const int RasterApronPixels = 2;

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
    /// The budget to fit, or <see langword="null"/> for <see cref="ResolveMaxBufferDimension()"/>.
    /// </param>
    /// <remarks>
    /// This belongs to allocation, not planning: a plan clamped to whichever device compiled it would mean
    /// something else on the next one, so planning keeps <see cref="MaxBufferDimension"/> and an allocation
    /// site whose density is its own re-clamps here. Such a site reports the density it allocated at, so the
    /// buffer and the density read back from it stay the same number.
    /// <para>
    /// A site whose density is the plan's cannot re-clamp: the render cache keys an entry on the planned
    /// materialization density and rejects a payload recorded at any other, so lowering the density there
    /// turns a cacheable fragment into a failed capture. Those sites keep the planned density and let the
    /// allocation refuse instead - see <see cref="FitsBufferBudget"/>, which degrades a preview and fails a
    /// delivery render through the lease session's existing contract.
    /// </para>
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
