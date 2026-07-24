using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

internal readonly record struct FilterEffectWorkingScalePolicy
{
    public FilterEffectWorkingScalePolicy(RenderScaleContract scale)
    {
        scale.ThrowIfUninitialized(nameof(scale));
        Scale = scale;
    }

    public RenderScaleContract Scale { get; }

    public object StructuralIdentity => Scale.StructuralIdentity;

    public EffectiveScale Resolve(
        IReadOnlyList<RenderFragmentReference> inputs,
        Rect outputBounds,
        float outputScale,
        float maxWorkingScale)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return Resolve(
            inputs.Select(static input => input.EffectiveScale).ToArray(),
            inputs.Select(static input => input.Bounds).ToArray(),
            outputBounds,
            outputScale,
            maxWorkingScale);
    }

    public EffectiveScale Resolve(
        IReadOnlyList<EffectiveScale> inputSupplies,
        IReadOnlyList<Rect> inputBounds,
        Rect outputBounds,
        float outputScale,
        float maxWorkingScale)
        => Resolve(
            inputSupplies,
            inputBounds,
            Enumerable.Repeat(outputBounds, inputSupplies.Count).ToArray(),
            outputScale,
            maxWorkingScale);

    public EffectiveScale Resolve(
        IReadOnlyList<EffectiveScale> inputSupplies,
        IReadOnlyList<Rect> inputBounds,
        IReadOnlyList<Rect> bufferBounds,
        float outputScale,
        float maxWorkingScale)
    {
        ArgumentNullException.ThrowIfNull(inputSupplies);
        ArgumentNullException.ThrowIfNull(inputBounds);
        ArgumentNullException.ThrowIfNull(bufferBounds);
        if (inputSupplies.Count == 0)
            throw new InvalidOperationException("A filter-effect working-scale policy requires at least one input.");
        if (inputSupplies.Count != inputBounds.Count)
            throw new ArgumentException("Filter-effect input supplies and bounds must have matching cardinality.");
        if (bufferBounds.Count == 0)
            throw new ArgumentException("A filter-effect operation requires at least one buffer footprint.");

        EffectiveScale[] mappedSupplies = new EffectiveScale[inputSupplies.Count];
        for (int index = 0; index < inputSupplies.Count; index++)
        {
            mappedSupplies[index] = Scale.Resolve(
                [inputSupplies[index]],
                inputBounds[index],
                outputScale,
                maxWorkingScale);
        }

        float workingScale = 0;
        bool hasConcreteScale = false;
        foreach (EffectiveScale mappedSupply in mappedSupplies)
        {
            if (mappedSupply.IsUnbounded)
                continue;

            workingScale = hasConcreteScale
                ? MathF.Max(workingScale, mappedSupply.Value)
                : mappedSupply.Value;
            hasConcreteScale = true;
        }

        if (!hasConcreteScale)
        {
            workingScale = MathF.Min(
                outputScale,
                RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale));
        }
        else
        {
            workingScale = MathF.Min(
                workingScale,
                RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale));
        }

        return EffectiveScale.At(ClampToBufferBudgets(bufferBounds, workingScale));
    }

    internal static EffectiveScale ResolveMaterialized(
        IReadOnlyList<EffectiveScale> inputSupplies,
        IReadOnlyList<Rect> bufferBounds,
        float outputScale,
        float maxWorkingScale)
    {
        ArgumentNullException.ThrowIfNull(inputSupplies);
        ArgumentNullException.ThrowIfNull(bufferBounds);
        if (inputSupplies.Count == 0)
            throw new InvalidOperationException("A materialized filter-effect operation requires at least one input.");
        if (bufferBounds.Count == 0)
            throw new ArgumentException("A materialized filter-effect operation requires at least one buffer footprint.");

        float workingScale = RenderScaleUtilities.ResolveWorkingScale(
            inputSupplies.ToArray(),
            outputScale,
            maxWorkingScale);
        return EffectiveScale.At(ClampToBufferBudgets(bufferBounds, workingScale));
    }

    internal static Rect[] CalculateLegacyBufferBounds(
        IReadOnlyList<Rect> inputBounds,
        IReadOnlyList<IFEItem> boundsItems,
        Rect fallbackBounds)
    {
        ArgumentNullException.ThrowIfNull(inputBounds);
        ArgumentNullException.ThrowIfNull(boundsItems);
        var result = new List<Rect>();
        int firstCustomIndex = -1;
        for (int index = 0; index < boundsItems.Count; index++)
        {
            if (boundsItems[index] is IFEItem_Custom)
            {
                firstCustomIndex = index;
                break;
            }
        }

        int branchItemCount = firstCustomIndex >= 0 ? firstCustomIndex : boundsItems.Count;
        Rect preCustomAggregateBounds = default;
        var preCustomBranchStates = new List<LegacyFootprintState>(inputBounds.Count);
        foreach (Rect input in inputBounds)
        {
            LegacyFootprintState branchState = CollectLegacyFootprints(
                input,
                boundsItems,
                startIndex: 0,
                itemCount: branchItemCount,
                fallbackBounds,
                result);
            preCustomAggregateBounds = preCustomAggregateBounds.Union(branchState.SemanticBounds);
            preCustomBranchStates.Add(branchState);
        }

        if (firstCustomIndex >= 0)
        {
            var preCustomRetainedBackingOffsets = new List<Rect>();
            foreach (LegacyFootprintState branchState in preCustomBranchStates)
            {
                foreach (Rect offset in branchState.RetainedBackingOffsets)
                {
                    Rect physicalBounds = offset.Translate(branchState.SemanticBounds.Position);
                    preCustomRetainedBackingOffsets.Add(physicalBounds.Translate(new Point(
                        -preCustomAggregateBounds.X,
                        -preCustomAggregateBounds.Y)));
                }
            }

            // A legacy Custom callback can combine or split the complete target list. Collapse from the union of
            // the actual per-target semantic results, not TransformBounds(inputUnion): arbitrary pure mappings need
            // not distribute over Union.
            CollectLegacyFootprints(
                preCustomAggregateBounds,
                boundsItems,
                firstCustomIndex,
                boundsItems.Count - firstCustomIndex,
                fallbackBounds,
                result,
                preCustomRetainedBackingOffsets,
                skipInitialCustomPreFlush: true);
        }

        if (result.Count == 0)
            result.Add(ToLocalLegacyFootprint(fallbackBounds, fallbackBounds));
        return result.ToArray();
    }

    private static LegacyFootprintState CollectLegacyFootprints(
        Rect initialSemanticBounds,
        IReadOnlyList<IFEItem> boundsItems,
        int startIndex,
        int itemCount,
        Rect fallbackBounds,
        List<Rect> result,
        IReadOnlyList<Rect>? initialRetainedBackingOffsets = null,
        bool skipInitialCustomPreFlush = false)
    {
        Rect semanticBounds = initialSemanticBounds;
        Rect allocationBounds = ToLocalLegacyFootprint(semanticBounds, fallbackBounds);
        Rect[] retainedBackingOffsets = initialRetainedBackingOffsets?.ToArray()
            ?? [CreateInitialRetainedBackingOffset(semanticBounds, fallbackBounds)];
        bool hasPendingSkiaWork = false;
        int endIndex = checked(startIndex + itemCount);
        for (int index = startIndex; index < endIndex; index++)
        {
            IFEItem item = boundsItems[index];
            switch (item)
            {
                case IFEItem_Skia:
                    Rect previousSemanticBounds = semanticBounds;
                    if (!semanticBounds.IsInvalid)
                        semanticBounds = item.TransformBounds(semanticBounds);
                    if (!allocationBounds.IsInvalid)
                        allocationBounds = item.TransformBounds(allocationBounds);
                    retainedBackingOffsets = TransformRetainedBackingOffsets(
                        retainedBackingOffsets,
                        previousSemanticBounds,
                        semanticBounds,
                        item,
                        fallbackBounds);
                    hasPendingSkiaWork = true;
                    break;
                case IFEItem_Custom:
                    if (!(skipInitialCustomPreFlush && index == startIndex))
                    {
                        result.Add(NormalizeLegacyAllocationBounds(allocationBounds, fallbackBounds));
                        AddRetainedBackingFootprints(
                            result,
                            semanticBounds,
                            retainedBackingOffsets,
                            fallbackBounds);
                    }
                    if (!semanticBounds.IsInvalid)
                        semanticBounds = item.TransformBounds(semanticBounds);
                    allocationBounds = ToLocalLegacyFootprint(semanticBounds, fallbackBounds);
                    result.Add(allocationBounds);
                    AddRetainedBackingFootprints(
                        result,
                        semanticBounds,
                        retainedBackingOffsets,
                        fallbackBounds);
                    hasPendingSkiaWork = false;
                    break;
                case FEItem_Shader:
                case FEItem_Geometry:
                    if (hasPendingSkiaWork)
                    {
                        result.Add(NormalizeLegacyAllocationBounds(allocationBounds, fallbackBounds));
                        AddRetainedBackingFootprints(
                            result,
                            semanticBounds,
                            retainedBackingOffsets,
                            fallbackBounds);
                    }
                    if (!semanticBounds.IsInvalid)
                        semanticBounds = item.TransformBounds(semanticBounds);
                    allocationBounds = NormalizeLegacySemanticBounds(semanticBounds, fallbackBounds);
                    retainedBackingOffsets =
                        [CreateInitialRetainedBackingOffset(semanticBounds, fallbackBounds)];
                    result.Add(allocationBounds);
                    hasPendingSkiaWork = false;
                    break;
                default:
                    Rect previousDefaultSemanticBounds = semanticBounds;
                    if (!semanticBounds.IsInvalid)
                        semanticBounds = item.TransformBounds(semanticBounds);
                    if (!allocationBounds.IsInvalid)
                        allocationBounds = item.TransformBounds(allocationBounds);
                    retainedBackingOffsets = TransformRetainedBackingOffsets(
                        retainedBackingOffsets,
                        previousDefaultSemanticBounds,
                        semanticBounds,
                        item,
                        fallbackBounds);
                    result.Add(NormalizeLegacyAllocationBounds(allocationBounds, fallbackBounds));
                    AddRetainedBackingFootprints(
                        result,
                        semanticBounds,
                        retainedBackingOffsets,
                        fallbackBounds);
                    hasPendingSkiaWork = false;
                    break;
            }
        }

        Rect normalizedAllocationBounds = NormalizeLegacyAllocationBounds(allocationBounds, fallbackBounds);
        Rect normalizedSemanticBounds = NormalizeLegacySemanticBounds(semanticBounds, fallbackBounds);
        result.Add(normalizedAllocationBounds);
        result.Add(normalizedSemanticBounds);
        result.Add(new Rect(
            normalizedSemanticBounds.Position,
            new Size(
                Math.Max(normalizedAllocationBounds.Width, normalizedSemanticBounds.Width),
                Math.Max(normalizedAllocationBounds.Height, normalizedSemanticBounds.Height))));
        AddRetainedBackingFootprints(
            result,
            normalizedSemanticBounds,
            retainedBackingOffsets,
            fallbackBounds);
        return new LegacyFootprintState(normalizedSemanticBounds, retainedBackingOffsets);
    }

    private static Rect CreateInitialRetainedBackingOffset(
        Rect semanticBounds,
        Rect fallbackBounds)
    {
        Rect normalizedSemanticBounds = NormalizeLegacySemanticBounds(semanticBounds, fallbackBounds);
        Rect scaleOneRasterBounds = PixelRect.FromRect(normalizedSemanticBounds, 1).ToRect(1);
        return scaleOneRasterBounds.Translate(new Point(
            -normalizedSemanticBounds.X,
            -normalizedSemanticBounds.Y));
    }

    private static Rect[] TransformRetainedBackingOffsets(
        IReadOnlyList<Rect> retainedBackingOffsets,
        Rect previousSemanticBounds,
        Rect semanticBounds,
        IFEItem item,
        Rect fallbackBounds)
    {
        Rect previous = NormalizeLegacySemanticBounds(previousSemanticBounds, fallbackBounds);
        Rect current = NormalizeLegacySemanticBounds(semanticBounds, fallbackBounds);
        var result = new Rect[retainedBackingOffsets.Count];
        for (int index = 0; index < retainedBackingOffsets.Count; index++)
        {
            Rect physicalBounds = retainedBackingOffsets[index].Translate(previous.Position);
            Rect transformed = item.TransformBounds(physicalBounds);
            Rect normalized = NormalizeLegacyAllocationBounds(transformed, fallbackBounds);
            result[index] = normalized.Translate(new Point(-current.X, -current.Y));
        }

        return result;
    }

    private static void AddRetainedBackingFootprints(
        List<Rect> result,
        Rect semanticBounds,
        IReadOnlyList<Rect> retainedBackingOffsets,
        Rect fallbackBounds)
    {
        Rect normalizedSemanticBounds = NormalizeLegacySemanticBounds(semanticBounds, fallbackBounds);
        foreach (Rect offset in retainedBackingOffsets)
        {
            result.Add(NormalizeLegacyAllocationBounds(
                offset.Translate(normalizedSemanticBounds.Position),
                fallbackBounds));
        }
    }

    private static Rect NormalizeLegacySemanticBounds(Rect bounds, Rect fallbackBounds)
        => bounds.IsInvalid ? fallbackBounds : bounds;

    private static Rect ToLocalLegacyFootprint(Rect bounds, Rect fallbackBounds)
    {
        Rect normalized = NormalizeLegacySemanticBounds(bounds, fallbackBounds);
        return new Rect(default(Point), normalized.Size);
    }

    private static Rect NormalizeLegacyAllocationBounds(Rect bounds, Rect fallbackBounds)
        => bounds.IsInvalid ? new Rect(default(Point), fallbackBounds.Size) : bounds;

    private static float ClampToBufferBudgets(
        IReadOnlyList<Rect> bufferBounds,
        float workingScale)
    {
        float result = workingScale;
        foreach (Rect bounds in bufferBounds)
        {
            result = MathF.Min(
                result,
                RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(bounds, workingScale));
        }

        return result;
    }

    private readonly record struct LegacyFootprintState(
        Rect SemanticBounds,
        IReadOnlyList<Rect> RetainedBackingOffsets);
}
