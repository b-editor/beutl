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

    /// <summary>Gets an optional working-input scale contract.</summary>
    /// <returns>
    /// A contract applied after input isolation, or <see langword="null"/> for supply-driven scale.
    /// </returns>
    /// <remarks>
    /// Override this hook instead of <see cref="Process"/>. The contract is folded into the first authored operation
    /// and evaluated per surviving branch; multi-input effects use the densest concrete result. It must be deterministic
    /// because symbolic metadata resolution may evaluate it again. Effects that author no operations remain pass-through.
    /// </remarks>
    protected virtual RenderScaleContract? GetWorkingScaleContract() => null;

    /// <summary>Reads each input's recorded metadata hint into one array.</summary>
    /// <remarks>
    /// Written out rather than passed to <c>Select</c> because the hint reader is an instance method: a
    /// method group over it builds a delegate on every recording, and this runs twice per recorded effect.
    /// </remarks>
    private static RenderFragmentMetadata[] RecordedMetadataHints(
        RenderNodeContext context,
        IReadOnlyList<RenderFragmentHandle> inputs)
    {
        var hints = new RenderFragmentMetadata[inputs.Count];
        for (int index = 0; index < hints.Length; index++)
            hints[index] = context.GetRecordedMetadataHint(inputs[index]);
        return hints;
    }

    public override void Process(RenderNodeContext context)
    {
        if (FilterEffect is not { } effectSnapshot || !effectSnapshot.Resource.IsEnabled)
        {
            context.PassThrough();
            return;
        }

        if (context.Inputs.Count == 0)
            return;

        FilterEffect.Resource effectResource = effectSnapshot.Resource;
        FilterEffect originalEffect = effectResource.GetOriginal()
            ?? throw new InvalidOperationException(
                "FilterEffectRenderNode cannot process a detached filter-effect resource. "
                + "Use an attached resource or override Process() in a custom render node that supports detached resources.");
        bool hasConcreteInputMetadata = context.TryCalculateInputBounds(out Rect inputBounds);
        Rect recordedInputBounds = hasConcreteInputMetadata
            ? inputBounds
            : context.CalculateRecordedInputBoundsHint();
        IReadOnlyList<RenderFragmentHandle> effectInputs = context.Inputs;
        bool requiresInputIsolation = false;
        for (int index = 0; index < effectInputs.Count; index++)
        {
            if (effectInputs[index].CanBeUsedAsValueInput)
                continue;

            requiresInputIsolation = true;
            break;
        }

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
            authorInputMetadata = RecordedMetadataHints(context, effectInputs);
        }
        float outputScale = context.OutputScale;
        float maxWorkingScale = context.MaxWorkingScale;

        FilterEffectWorkingScalePolicy? workingScalePolicy = null;
        FilterEffectWorkingScalePolicy GetOrCreateWorkingScalePolicy()
            => workingScalePolicy ??= new FilterEffectWorkingScalePolicy(
                GetWorkingScaleContract() ?? RenderScaleContract.MaterializeAtWorkingScale);

        bool hasResolvedWorkingScale = hasConcreteInputMetadata && authorInputMetadata.Length == 1;
        FilterEffectContext recordingContext;
        if (hasResolvedWorkingScale)
        {
            recordingContext = new FilterEffectContext(
                inputBounds,
                context.OutputScale,
                () => ResolveWorkingScale(
                    authorInputMetadata,
                    authorInputMetadata.SelectToArray(static item => item.Bounds),
                    outputScale,
                    maxWorkingScale,
                    GetOrCreateWorkingScalePolicy()),
                context);
        }
        else
        {
            recordingContext = new FilterEffectContext(
                hasConcreteInputMetadata ? inputBounds : Rect.Invalid,
                context.OutputScale,
                workingScale: default,
                renderContext: context,
                hasResolvedWorkingScale: false);
        }
        try
        {
            recordingContext.ApplyTransactional(originalEffect, effectResource);
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
                    hasFiniteIsolationDomain
                        ? context.Layer(effectInputs, isolationDomain)
                        : context.OwningTargetLayer(effectInputs),
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
                RenderFragmentMetadata[] segmentInputMetadata = RecordedMetadataHints(context, current);
                Rect[] segmentBufferBounds = FilterEffectWorkingScalePolicy.CalculateEffectItemBufferBounds(
                        segmentInputMetadata.SelectToArray(static item => item.Bounds),
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
                        var shaderOutputs = new RenderFragmentHandle[current.Count];
                        for (int index = 0; index < shaderOutputs.Length; index++)
                        {
                            shaderOutputs[index] = context.Shader(
                                current[index],
                                shader.Description,
                                pendingWorkingScalePolicy);
                        }

                        current = shaderOutputs;
                        pendingWorkingScalePolicy = null;
                        break;
                    case FEItem_Geometry geometry when !opaqueTail:
                        FlushEffectItems();
                        var geometryOutputs = new RenderFragmentHandle[current.Count];
                        for (int index = 0; index < geometryOutputs.Length; index++)
                        {
                            geometryOutputs[index] = context.Geometry(
                                current[index],
                                geometry.Description,
                                pendingWorkingScalePolicy);
                        }

                        current = geometryOutputs;
                        pendingWorkingScalePolicy = null;
                        break;
                    default:
                        AppendEffectItem(item, itemIndex);
                        break;
                }
            }

            FlushEffectItems();
            context.PublishRange(current);
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
                metadata.SelectToArray(static item => item.EffectiveScale),
                metadata.SelectToArray(static item => item.Bounds),
                bufferBounds,
                outputScale,
                maxWorkingScale).Value;
        }

        return FilterEffectWorkingScalePolicy.ResolveMaterialized(
            metadata.SelectToArray(static item => item.EffectiveScale),
            bufferBounds,
            outputScale,
            maxWorkingScale).Value;
    }

}
