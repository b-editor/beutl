using Beutl.Engine;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

public class FilterEffectRenderNode(FilterEffect.Resource filterEffect) : ContainerRenderNode
{
    public (FilterEffect.Resource Resource, int Version)? FilterEffect { get; private set; } = filterEffect.Capture();

    public bool Update(FilterEffect.Resource? fe)
    {
        if (!fe.Compare(FilterEffect))
        {
            FilterEffect = fe.Capture();
            HasChanges = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets an optional declarative scale contract for this effect's working inputs.
    /// </summary>
    /// <returns>
    /// A scale contract applied after the base node has isolated target-dependent inputs, or
    /// <see langword="null"/> to use the standard supply-driven working scale.
    /// </returns>
    /// <remarks>
    /// Override this hook for working-scale customization instead of replacing <see cref="Process"/>. The returned
    /// contract is folded into the first authored shader, geometry, or legacy operation. Its callback receives one
    /// surviving branch at a time, with one <see cref="RenderScaleContext.InputSupplies"/> item and that branch's
    /// isolated effect-input bounds as <see cref="RenderScaleContext.OutputBounds"/>. Legacy multi-input operations
    /// aggregate the densest concrete branch result and fall back to <see cref="RenderScaleContext.OutputScale"/>
    /// only when every branch remains unbounded. Allocation clamping is independent of callback cardinality: it
    /// covers each branch's local-origin footprint and every intermediate legacy materialization. Because a custom
    /// legacy callback may combine or split targets without declaring topology, the first such callback collapses
    /// transformed branch results to their union and footprints after it conservatively use that aggregate domain.
    /// The callback may be evaluated again after symbolic
    /// input metadata is resolved, so it must be deterministic and side-effect-free. An effect that authors no
    /// operations creates no isolation or contract fragment and remains a true pass-through. The hook and resolver
    /// stay lazy and are not evaluated for such an effect unless its <c>ApplyTo</c> implementation explicitly probes
    /// <see cref="FilterEffectContext.WorkingScale"/> or <see cref="FilterEffectContext.TryGetWorkingScale"/>.
    /// </remarks>
    protected virtual RenderScaleContract? GetWorkingScaleContract() => null;

    public override void Process(RenderNodeContext context)
    {
        if (FilterEffect is not { } effectSnapshot || !effectSnapshot.Resource.IsEnabled)
        {
            context.PassThrough();
            return;
        }

        if (context.Inputs.Count == 0)
            return;

        bool hasConcreteInputMetadata = context.TryCalculateInputBounds(out Rect inputBounds);
        Rect recordedInputBounds = hasConcreteInputMetadata
            ? inputBounds
            : context.CalculateRecordedInputBoundsHint();
        IReadOnlyList<RenderFragmentHandle> effectInputs = context.Inputs;
        bool requiresInputIsolation = effectInputs.Any(static input => !input.CanBeUsedAsValueInput);
        bool hasFiniteIsolationDomain = false;
        Rect isolationDomain = default;
        RenderFragmentMetadata[] authorInputMetadata;
        if (requiresInputIsolation)
        {
            if (context.TryCalculateFiniteIsolationDomain(out isolationDomain))
            {
                if (isolationDomain.Width == 0 || isolationDomain.Height == 0)
                {
                    context.PassThrough();
                    return;
                }

                hasFiniteIsolationDomain = true;
                inputBounds = isolationDomain;
                hasConcreteInputMetadata = true;
                recordedInputBounds = isolationDomain;
            }
            else
            {
                inputBounds = default;
                hasConcreteInputMetadata = false;
            }

            authorInputMetadata =
            [
                new RenderFragmentMetadata(recordedInputBounds, EffectiveScale.Unbounded),
            ];
        }
        else
        {
            authorInputMetadata = effectInputs
                .Select(context.GetRecordedMetadataHint)
                .ToArray();
        }
        float outputScale = context.OutputScale;
        float maxWorkingScale = context.MaxWorkingScale;

        FilterEffectWorkingScalePolicy? workingScalePolicy = null;
        FilterEffectWorkingScalePolicy GetOrCreateWorkingScalePolicy()
            => workingScalePolicy ??= new FilterEffectWorkingScalePolicy(
                GetWorkingScaleContract() ?? RenderScaleContract.MaterializeAtWorkingScale);

        FilterEffectContext? effectContext = new(
            hasConcreteInputMetadata ? inputBounds : Rect.Invalid,
            context.OutputScale,
            () => ResolveWorkingScale(
                authorInputMetadata,
                authorInputMetadata.Select(static item => item.Bounds).ToArray(),
                outputScale,
                maxWorkingScale,
                GetOrCreateWorkingScalePolicy()),
            context,
            hasResolvedWorkingScale: hasConcreteInputMetadata && authorInputMetadata.Length == 1);
        try
        {
            FilterEffect.Resource effectResource = effectSnapshot.Resource;
            effectContext.ApplyTransactional(effectResource.GetOriginal(), effectResource);
            IReadOnlyList<IFEItem> items = effectContext.GetOrderedItems();
            if (items.Count == 0)
            {
                context.PassThrough();
                return;
            }

            FilterEffectWorkingScalePolicy resolvedWorkingScalePolicy = GetOrCreateWorkingScalePolicy();
            if (requiresInputIsolation)
            {
                effectInputs = hasFiniteIsolationDomain
                    ? [context.Layer(effectInputs, isolationDomain)]
                    : [context.OwningTargetLayer(effectInputs)];
            }

            IReadOnlyList<RenderFragmentHandle> current = effectInputs;
            FilterEffectWorkingScalePolicy? pendingWorkingScalePolicy = resolvedWorkingScalePolicy;
            var legacyItems = new List<IFEItem>();
            int legacySegment = 0;
            Rect legacyBounds = default;
            bool legacyBoundsInitialized = false;
            bool opaqueTail = false;

            void AppendLegacyItem(IFEItem item)
            {
                if (!legacyBoundsInitialized)
                {
                    legacyBounds = CalculateRecordedBoundsHint(context, current);
                    legacyBoundsInitialized = true;
                }

                legacyItems.Add(item);
                if (!legacyBounds.IsInvalid)
                    legacyBounds = item.TransformBounds(legacyBounds);
                opaqueTail |= legacyBounds.IsInvalid;
            }

            void FlushLegacyItems()
            {
                if (legacyItems.Count == 0 || current.Count == 0)
                    return;

                Rect segmentInputBounds = CalculateRecordedBoundsHint(context, current);
                RenderFragmentMetadata[] segmentInputMetadata = current
                    .Select(context.GetRecordedMetadataHint)
                    .ToArray();
                Rect[] segmentBufferBounds = FilterEffectWorkingScalePolicy.CalculateLegacyBufferBounds(
                        segmentInputMetadata.Select(static item => item.Bounds).ToArray(),
                        legacyItems,
                        legacyBounds.IsInvalid ? segmentInputBounds : legacyBounds);
                FilterEffectContext? segment = FilterEffectContext.CreateLegacySegment(
                    segmentInputBounds,
                    context.OutputScale,
                    ResolveWorkingScale(
                        segmentInputMetadata,
                        segmentBufferBounds,
                        outputScale,
                        maxWorkingScale,
                        pendingWorkingScalePolicy),
                    legacyItems);
                try
                {
                    Rect segmentOutputBounds = segment.Bounds;
                    bool requiresOwningTargetDomain = segmentOutputBounds.IsInvalid;
                    if (requiresOwningTargetDomain)
                        segmentOutputBounds = segmentInputBounds;
                    RenderResource<FilterEffectContext> segmentResource = context.Own(
                        segment,
                        (
                            typeof(FilterEffectRenderNode),
                            effectResource.GetOriginal().Id,
                            legacySegment++),
                        effectSnapshot.Version);
                    segment = null;
                    current =
                    [
                        context.LegacyFilterEffect(
                            current,
                            segmentResource,
                            segmentOutputBounds,
                            requiresOwningTargetDomain,
                            legacyItems,
                            pendingWorkingScalePolicy),
                    ];
                    pendingWorkingScalePolicy = null;
                }
                finally
                {
                    segment?.Dispose();
                    legacyItems.Clear();
                    legacyBounds = default;
                    legacyBoundsInitialized = false;
                    opaqueTail = false;
                }
            }

            foreach (IFEItem item in items)
            {
                switch (item)
                {
                    case FEItem_Shader shader when !opaqueTail:
                        FlushLegacyItems();
                        current = current
                            .Select(input => context.Shader(
                                input,
                                shader.Description,
                                pendingWorkingScalePolicy))
                            .ToArray();
                        pendingWorkingScalePolicy = null;
                        break;
                    case FEItem_Geometry geometry when !opaqueTail:
                        FlushLegacyItems();
                        current = current
                            .Select(input => context.Geometry(
                                input,
                                geometry.Description,
                                pendingWorkingScalePolicy))
                            .ToArray();
                        pendingWorkingScalePolicy = null;
                        break;
                    default:
                        AppendLegacyItem(item);
                        break;
                }
            }

            FlushLegacyItems();
            context.PublishRange(current);
            effectContext.TransferResources();
        }
        finally
        {
            effectContext?.Dispose();
        }
    }

    private static Rect CalculateRecordedBoundsHint(
        RenderNodeContext context,
        IReadOnlyList<RenderFragmentHandle> inputs)
    {
        Rect result = default;
        foreach (RenderFragmentHandle input in inputs)
            result = result.Union(context.GetRecordedMetadataHint(input).Bounds);
        return result;
    }

    private static float ResolveWorkingScale(
        IReadOnlyList<RenderFragmentMetadata> metadata,
        IReadOnlyList<Rect> bufferBounds,
        float outputScale,
        float maxWorkingScale,
        FilterEffectWorkingScalePolicy? workingScalePolicy = null)
    {
        if (workingScalePolicy is { } policy)
        {
            return policy.Resolve(
                metadata.Select(static item => item.EffectiveScale).ToArray(),
                metadata.Select(static item => item.Bounds).ToArray(),
                bufferBounds,
                outputScale,
                maxWorkingScale).Value;
        }

        return FilterEffectWorkingScalePolicy.ResolveMaterialized(
            metadata.Select(static item => item.EffectiveScale).ToArray(),
            bufferBounds,
            outputScale,
            maxWorkingScale).Value;
    }

}
