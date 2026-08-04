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
    /// covers each branch's local-origin footprint and every intermediate legacy materialization. The forced Flush
    /// immediately before a custom callback removes renderer-owned aprons and presents each branch through the
    /// historical dimension-sized local backing. Because that callback may then combine, split, move, or shrink
    /// targets without declaring topology, its results collapse to their union and later footprints conservatively
    /// use that aggregate domain while retaining physical backing produced by the callback.
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

        FilterEffectContext recordingContext = new(
            hasConcreteInputMetadata ? inputBounds : Rect.Invalid,
            recordedInputBounds,
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
            recordingContext.ApplyTransactional(effectResource.GetOriginal(), effectResource);
            IReadOnlyList<IFEItem> items = recordingContext.GetOrderedItems();
            if (items.Count == 0)
            {
                context.PassThrough();
                return;
            }

            IReadOnlyList<RegisteredEffectBrush> registeredBrushes = recordingContext.RegisteredBrushes;
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
            int legacyLastItemIndex = -1;

            void AppendLegacyItem(IFEItem item, int itemIndex)
            {
                if (!legacyBoundsInitialized)
                {
                    legacyBounds = CalculateRecordedBoundsHint(context, current);
                    legacyBoundsInitialized = true;
                }

                legacyItems.Add(item);
                legacyLastItemIndex = itemIndex;
                // A deferred-bound item resolves at execution time; authoring it against the
                // provisional hint would freeze the wrong matrix, so the segment stays symbolic.
                if (!legacyBounds.IsInvalid && item is not IFEItem_Skia { ResolveBoundsAtExecutionTime: true })
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
                    legacyItems,
                    recordingContext.NestedBrushLoweringFailure);
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
                            pendingWorkingScalePolicy,
                            SelectSegmentBrushes(registeredBrushes, legacyLastItemIndex)),
                    ];
                    pendingWorkingScalePolicy = null;
                }
                finally
                {
                    segment?.Dispose();
                    legacyItems.Clear();
                    legacyLastItemIndex = -1;
                    legacyBounds = default;
                    legacyBoundsInitialized = false;
                    opaqueTail = false;
                }
            }

            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                IFEItem item = items[itemIndex];
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
                        AppendLegacyItem(item, itemIndex);
                        break;
                }
            }

            FlushLegacyItems();
            context.PublishRange(current);
            recordingContext.TransferResources();
        }
        finally
        {
            recordingContext.Dispose();
        }
    }

    // A segment fragment takes a hard dependency on every brush it is given. A handle stays usable from every
    // operation authored after it was registered — the recorder cannot see which of them actually paints with it —
    // so a segment takes exactly the brushes registered before its own last operation, and no later one.
    // The selection therefore over-approximates: a segment may resolve a brush it never dereferences. It cannot be
    // narrowed to the operations a segment appears to orphan, because RegisterBrush dedupes by identity, so one
    // handle can be painted by operations on both sides of a typed operation.
    private static IReadOnlyList<RegisteredEffectBrush> SelectSegmentBrushes(
        IReadOnlyList<RegisteredEffectBrush> registeredBrushes,
        int lastItemIndex)
    {
        if (registeredBrushes.Count == 0)
            return registeredBrushes;

        return registeredBrushes.Where(brush => brush.FirstUsableItemIndex <= lastItemIndex).ToArray();
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
