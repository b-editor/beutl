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
            MarkChanged();
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
    /// contract is folded into the first authored shader, geometry, or effect-item operation. Its callback receives one
    /// surviving branch at a time, with one <see cref="RenderScaleContext.InputSupplies"/> item and that branch's
    /// isolated effect-input bounds as <see cref="RenderScaleContext.OutputBounds"/>. EffectItem multi-input operations
    /// aggregate the densest concrete branch result and fall back to <see cref="RenderScaleContext.OutputScale"/>
    /// only when every branch remains unbounded. Allocation clamping is independent of callback cardinality: it
    /// covers each branch's local-origin footprint and every intermediate effect-item materialization. The forced Flush
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

    /// <summary>
    /// Gets whether an effect input must be isolated into an off-screen layer before the effect consumes it.
    /// </summary>
    /// <param name="input">A non-null effect input recorded for the current <see cref="Process"/> call.</param>
    /// <returns><see langword="true"/> to isolate the input set before lowering.</returns>
    /// <remarks>
    /// The base implementation isolates exactly when an input cannot be consumed as a materialized value
    /// (<see cref="RenderFragmentHandle.CanBeUsedAsValueInput"/>). Every input is asked, and the node isolates when
    /// any of them answers <see langword="true"/>, so an override may widen isolation — an effect that must always
    /// read flattened pixels, for instance — but must never narrow it. Answering <see langword="false"/> for an
    /// input the base implementation would isolate hands the effect a fragment it cannot sample. Add conditions to
    /// the base answer; do not replace it.
    /// </remarks>
    protected virtual bool RequiresInputIsolation(RenderFragmentHandle input)
        => !input.CanBeUsedAsValueInput;

    /// <summary>Isolates the effect inputs into the single fragment the effect then consumes.</summary>
    /// <param name="context">The active recording context for the current <see cref="Process"/> call.</param>
    /// <param name="inputs">The non-null, non-empty ordered effect inputs to isolate.</param>
    /// <param name="isolationDomain">
    /// The finite domain resolved by <see cref="RenderNodeContext.TryCalculateFiniteIsolationDomain"/>, or
    /// <see cref="Rect.Invalid"/> when no finite domain was resolved.
    /// </param>
    /// <returns>A non-null single fragment standing in for every entry of <paramref name="inputs"/>.</returns>
    /// <remarks>
    /// Called only when <see cref="RequiresInputIsolation"/> selected isolation. The base implementation records a
    /// finite <see cref="RenderNodeContext.Layer(IReadOnlyList{RenderFragmentHandle}, Rect, bool)"/> over the
    /// domain, and falls back to
    /// <see cref="RenderNodeContext.OwningTargetLayer(IReadOnlyList{RenderFragmentHandle})"/> when the domain is
    /// <see cref="Rect.Invalid"/>. An override may re-scope or wrap that isolation, but the returned fragment must
    /// still represent <em>all</em> of <paramref name="inputs"/>: it becomes the effect's only view of them, so an
    /// input dropped here is dropped from the rendered result. It must also be value-eligible, because the effect
    /// samples it.
    /// </remarks>
    protected virtual RenderFragmentHandle IsolateInputs(
        RenderNodeContext context,
        IReadOnlyList<RenderFragmentHandle> inputs,
        Rect isolationDomain)
        => isolationDomain.IsInvalid
            ? context.OwningTargetLayer(inputs)
            : context.Layer(inputs, isolationDomain);

    /// <summary>Publishes the lowered effect result as this node's output.</summary>
    /// <param name="context">The active recording context for the current <see cref="Process"/> call.</param>
    /// <param name="lowered">The non-null ordered fragments the effect lowered to.</param>
    /// <remarks>
    /// The final step of <see cref="Process"/>, called once after every effect item has been lowered. The base
    /// implementation publishes the fragments unchanged through
    /// <see cref="RenderNodeContext.PublishRange(IEnumerable{RenderFragmentHandle})"/>. An override may present
    /// them differently first — inside its own
    /// <see cref="RenderNodeContext.TargetLayerScope(IReadOnlyList{RenderFragmentHandle}, TargetRegion)"/>, a
    /// further layer, or an added operation — but it must publish the result of doing so. A fragment that is
    /// neither published nor dropped fails recording, and publishing nothing renders the effect away entirely.
    /// An override re-presents <paramref name="lowered"/>; it never discards it.
    /// </remarks>
    protected virtual void PublishLoweredResult(
        RenderNodeContext context,
        IReadOnlyList<RenderFragmentHandle> lowered)
        => context.PublishRange(lowered);

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
        bool requiresInputIsolation = effectInputs.Any(input => RequiresInputIsolation(input));
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
            recordingContext.ApplyTransactional(effectResource.GetOriginal()!, effectResource);
            IReadOnlyList<IFEItem> items = recordingContext.GetOrderedItems();
            if (items.Count == 0)
            {
                context.PassThrough();
                return;
            }

            FilterEffectWorkingScalePolicy resolvedWorkingScalePolicy = GetOrCreateWorkingScalePolicy();
            if (requiresInputIsolation)
            {
                effectInputs =
                [
                    IsolateInputs(
                        context,
                        effectInputs,
                        hasFiniteIsolationDomain ? isolationDomain : Rect.Invalid),
                ];
            }

            IReadOnlyList<RenderFragmentHandle> current = effectInputs;
            FilterEffectWorkingScalePolicy? pendingWorkingScalePolicy = resolvedWorkingScalePolicy;
            var effectItems = new List<IFEItem>();
            Rect effectItemBounds = default;
            bool effectItemBoundsInitialized = false;
            bool opaqueTail = false;

            void AppendEffectItem(IFEItem item, int itemIndex)
            {
                if (!effectItemBoundsInitialized)
                {
                    effectItemBounds = CalculateRecordedBoundsHint(context, current);
                    effectItemBoundsInitialized = true;
                }

                effectItems.Add(item);
                // A deferred-bound item resolves at execution time; authoring it against the
                // provisional hint would freeze the wrong matrix, so the segment stays symbolic.
                if (!effectItemBounds.IsInvalid && item is not IFEItem_Skia { ResolveBoundsAtExecutionTime: true })
                    effectItemBounds = item.TransformBounds(effectItemBounds);
                opaqueTail |= effectItemBounds.IsInvalid;
            }

            void FlushEffectItems()
            {
                if (effectItems.Count == 0 || current.Count == 0)
                    return;

                Rect segmentInputBounds = CalculateRecordedBoundsHint(context, current);
                RenderFragmentMetadata[] segmentInputMetadata = current
                    .Select(context.GetRecordedMetadataHint)
                    .ToArray();
                Rect[] segmentBufferBounds = FilterEffectWorkingScalePolicy.CalculateEffectItemBufferBounds(
                        segmentInputMetadata.Select(static item => item.Bounds).ToArray(),
                        effectItems,
                        effectItemBounds.IsInvalid ? segmentInputBounds : effectItemBounds);
                FilterEffectContext? segment = FilterEffectContext.CreateEffectItemSegment(
                    segmentInputBounds,
                    context.OutputScale,
                    ResolveWorkingScale(
                        segmentInputMetadata,
                        segmentBufferBounds,
                        outputScale,
                        maxWorkingScale,
                        pendingWorkingScalePolicy),
                    effectItems);
                try
                {
                    Rect segmentOutputBounds = segment.Bounds;
                    bool requiresOwningTargetDomain = segmentOutputBounds.IsInvalid;
                    if (requiresOwningTargetDomain)
                        segmentOutputBounds = segmentInputBounds;
                    RenderResource<FilterEffectContext> segmentResource = context.Own(segment);
                    segment = null;
                    current =
                    [
                        context.FilterEffectSegment(
                            current,
                            segmentResource,
                            segmentOutputBounds,
                            requiresOwningTargetDomain,
                            effectItems,
                            pendingWorkingScalePolicy),
                    ];
                    pendingWorkingScalePolicy = null;
                }
                finally
                {
                    segment?.Dispose();
                    effectItems.Clear();
                    effectItemBounds = default;
                    effectItemBoundsInitialized = false;
                    opaqueTail = false;
                }
            }

            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                IFEItem item = items[itemIndex];
                switch (item)
                {
                    case FEItem_Shader shader when !opaqueTail:
                        FlushEffectItems();
                        current = current
                            .Select(input => context.Shader(
                                input,
                                shader.Description,
                                pendingWorkingScalePolicy))
                            .ToArray();
                        pendingWorkingScalePolicy = null;
                        break;
                    case FEItem_Geometry geometry when !opaqueTail:
                        FlushEffectItems();
                        current = current
                            .Select(input => context.Geometry(
                                input,
                                geometry.Description,
                                pendingWorkingScalePolicy))
                            .ToArray();
                        pendingWorkingScalePolicy = null;
                        break;
                    default:
                        AppendEffectItem(item, itemIndex);
                        break;
                }
            }

            FlushEffectItems();
            PublishLoweredResult(context, current);
            recordingContext.TransferResources();
        }
        finally
        {
            recordingContext.Dispose();
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
