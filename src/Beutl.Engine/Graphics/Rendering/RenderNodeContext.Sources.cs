using System.Collections.Immutable;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed partial class RenderNodeContext
{
    /// <summary>Records a source fragment painted through an <see cref="ImmediateCanvas"/>.</summary>
    /// <typeparam name="TState">The callback state type.</typeparam>
    /// <param name="state">Immutable state retained until the deferred callback runs.</param>
    /// <param name="draw">A non-null static painting callback; see <see cref="PaintedSourceDraw{TState}"/>.</param>
    /// <param name="fill">A request-borrowed fill, or <see langword="null"/>.</param>
    /// <param name="pen">A request-borrowed stroke pen, or <see langword="null"/>.</param>
    /// <param name="outputBounds">Finite, non-empty local bounds. Use <see cref="PenHelper"/> for strokes.</param>
    /// <param name="hitTest">An initialized hit-test contract describing which points the source claims.</param>
    /// <param name="scale">An initialized scale contract for repaintable or materialized content.</param>
    /// <param name="directReplayAtExactIntegerReduction">
    /// Whether the source may still be replayed directly when the surrounding transform reduces it by an exact
    /// integer factor. Pass <see langword="false"/> unless re-painting at the reduced size is what the source
    /// wants; that routes such a reduction through an intermediate so the downsample is filtered. It carries no
    /// default here on purpose: naming it is what selects this overload over the public one beside it.
    /// </param>
    /// <param name="deviceGridSensitivity">
    /// Whether device-grid phase affects the pixels. Analytically anti-aliased content is phase-dependent.
    /// </param>
    /// <param name="supportsDirectDstOut">
    /// Whether destination-out may paint directly. Overlapping coverage requires <see langword="false"/>.
    /// </param>
    /// <param name="resources">
    /// Optional additional declared resources this source depends on, on top of the fill and the pen. Every entry
    /// must already belong to the active request family.
    /// </param>
    /// <param name="rasterOutset">
    /// Extra padding, in the node's own coordinate space, that the rasterizer adds around
    /// <paramref name="outputBounds"/> so filtering or anti-aliasing that spills past the declared bounds is not
    /// clipped. Leave it default when the callback paints strictly inside its bounds.
    /// </param>
    /// <returns>A new transaction-scoped source fragment. The result is not published automatically.</returns>
    /// <remarks>
    /// Valid only during <see cref="RenderNode.Process(RenderNodeContext)"/>, on the recording context passed to that
    /// call. This is the engine-side overload, and it keeps two things the public one beside it withholds:
    /// <paramref name="directReplayAtExactIntegerReduction"/>, which names a planner fast path an out-of-tree node
    /// has no model of and cannot decide for itself, and a bare <paramref name="resources"/> list converted to
    /// engine-only bindings that no declared hit test can address.
    /// </remarks>
    internal RenderFragmentHandle PaintedSource<TState>(
        TState state,
        PaintedSourceDraw<TState> draw,
        Brush.Resource? fill,
        Pen.Resource? pen,
        Rect outputBounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        bool directReplayAtExactIntegerReduction,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        bool supportsDirectDstOut = true,
        IReadOnlyList<RenderResource>? resources = null,
        Thickness rasterOutset = default)
    {
        ArgumentNullException.ThrowIfNull(draw);
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        RenderDescriptionValidation.ThrowUnlessFiniteNonEmpty(outputBounds, nameof(outputBounds));

        // The result is sized from the declaration rather than grown into by a projection. Declaring none is
        // what every painted primitive that only fills and strokes does, so that case shares the empty array.
        IReadOnlyList<RenderResource> declared = resources ?? Array.Empty<RenderResource>();
        RenderDescriptionValidation.ThrowIfResourcesUndeclarable(declared, nameof(resources));
        RenderResourceBinding[] engineBindings = declared.Count == 0
            ? []
            : new RenderResourceBinding[declared.Count];
        for (int index = 0; index < engineBindings.Length; index++)
            engineBindings[index] = RenderResourceBinding.CreateEngineBinding(declared[index]);

        return PaintedSourceCore(
            state,
            draw,
            fill,
            pen,
            OpaqueRenderBoundsContract.Source(outputBounds, rasterOutset),
            hitTest,
            scale,
            directReplayAtExactIntegerReduction,
            deviceGridSensitivity,
            supportsDirectDstOut,
            engineBindings);
    }


    /// <summary>
    /// Records a source fragment that paints itself with a fill brush and a stroke pen through an
    /// <see cref="ImmediateCanvas"/>.
    /// </summary>
    /// <typeparam name="TState">The type of the state handed back to <paramref name="draw"/> unchanged.</typeparam>
    /// <param name="state">
    /// The state the callback paints from. Treat it as immutable once recorded: the callback runs later, so a value
    /// mutated after this call changes what the fragment paints without the engine noticing.
    /// </param>
    /// <param name="draw">
    /// A non-null painting callback. Declare it as a static lambda so it carries no per-frame identity; see
    /// <see cref="PaintedSourceDraw{TState}"/>.
    /// </param>
    /// <param name="fill">
    /// The fill brush the callback receives, or <see langword="null"/> for an unfilled source. A non-null brush is
    /// borrowed for the request, so the caller keeps ownership of it.
    /// </param>
    /// <param name="pen">
    /// The stroke pen the callback receives, or <see langword="null"/> for an unstroked source. A non-null pen is
    /// borrowed for the request, so the caller keeps ownership of it.
    /// </param>
    /// <param name="outputBounds">
    /// The finite, non-empty bounds the callback paints within, in the node's own coordinate space. Compute stroked
    /// bounds with <see cref="PenHelper.GetBounds(Rect, Pen.Resource)"/> so they follow the same stroke-alignment and
    /// offset convention as the built-in shape nodes.
    /// </param>
    /// <param name="hitTest">An initialized hit-test contract describing which points the source claims.</param>
    /// <param name="scale">
    /// An initialized scale contract. Use <see cref="RenderScaleContract.Vector"/> for content the callback can
    /// re-paint at any density, and a materializing contract for content that is only correct at its working scale.
    /// </param>
    /// <param name="deviceGridSensitivity">
    /// Whether the painted pixels depend on where the device pixel grid falls. Keep the
    /// <see cref="RenderDeviceGridSensitivity.PhaseDependent"/> default for analytically anti-aliased content, and
    /// declare <see cref="RenderDeviceGridSensitivity.Insensitive"/> only when a sub-pixel shift of the grid cannot
    /// change the output.
    /// </param>
    /// <param name="supportsDirectDstOut">
    /// Whether the source may be painted straight into a destination-out composite instead of an isolated layer.
    /// Set it to <see langword="false"/> when the callback paints overlapping coverage that would double up.
    /// </param>
    /// <param name="bindings">
    /// Additional slot-addressed resources, or <see langword="null"/>.
    /// </param>
    /// <param name="rasterOutset">
    /// Local buffer-only padding for filtering or anti-aliasing outside <paramref name="outputBounds"/>.
    /// </param>
    /// <returns>A new transaction-scoped source fragment. The result is not published automatically.</returns>
    /// <remarks>Valid only during the active <see cref="RenderNode.Process(RenderNodeContext)"/> call.</remarks>
    public RenderFragmentHandle PaintedSource<TState>(
        TState state,
        PaintedSourceDraw<TState> draw,
        Brush.Resource? fill,
        Pen.Resource? pen,
        Rect outputBounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        bool supportsDirectDstOut = true,
        IReadOnlyList<RenderResourceBinding>? bindings = null,
        Thickness rasterOutset = default)
        where TState : notnull
    {
        ArgumentNullException.ThrowIfNull(draw);
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        RenderDescriptionValidation.ThrowUnlessFiniteNonEmpty(outputBounds, nameof(outputBounds));
        IReadOnlyList<RenderResourceBinding> declaredBindings = bindings ?? Array.Empty<RenderResourceBinding>();
        RenderDescriptionValidation.ThrowIfBindingsUndeclarable(declaredBindings, nameof(bindings));

        return PaintedSourceCore(
            state,
            draw,
            fill,
            pen,
            OpaqueRenderBoundsContract.Source(outputBounds, rasterOutset),
            hitTest,
            scale,
            directReplayAtExactIntegerReduction: false,
            deviceGridSensitivity,
            supportsDirectDstOut,
            declaredBindings);
    }

    private RenderFragmentHandle PaintedSourceCore<TState>(
        TState state,
        PaintedSourceDraw<TState> draw,
        Brush.Resource? fill,
        Pen.Resource? pen,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        bool directReplayAtExactIntegerReduction,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        bool supportsDirectDstOut,
        IReadOnlyList<RenderResourceBinding> declaredBindings)
    {
        GetTransaction();

        // Sized for what goes in it: the declared bindings, then the fill and the pen where present. A
        // list sized to the declaration alone reallocates on the first of those two appends.
        var bindings = new RenderResourceBinding[
            declaredBindings.Count + (fill is null ? 0 : 1) + (pen is null ? 0 : 1)];
        for (int index = 0; index < declaredBindings.Count; index++)
            bindings[index] = declaredBindings[index];

        int appended = declaredBindings.Count;
        if (fill is not null)
            bindings[appended++] = RenderResourceBinding.CreateEngineBinding(Borrow(fill));
        if (pen is not null)
            bindings[appended++] = RenderResourceBinding.CreateEngineBinding(Borrow(pen));

        var source = new PlainPaintedSource<TState>(
            state,
            draw,
            fill,
            pen,
            DistinctResources(bindings));
        // Both callbacks are static, so the description's identity is the pair of declarations rather than
        // this frame's helper instance, which a method group over `source` would have made it.
        Action<EngineDirectRenderSession, PlainPaintedSource<TState>>? directReplay =
            ContainsDrawableBrush(fill, pen)
                ? null
                : static (session, source) => source.ExecuteDirect(session);
        OpaqueRenderDescription description = OpaqueRenderDescription.CreateEngineSource(
            state: source,
            execute: static (session, source) => source.Execute(session),
            directReplay: directReplay,
            bounds: bounds,
            hitTest: hitTest,
            scale: scale,
            directReplayAtExactIntegerReduction: directReplayAtExactIntegerReduction,
            deviceGridSensitivity: deviceGridSensitivity,
            supportsDirectDstOut: supportsDirectDstOut,
            resources: bindings);
        return OpaqueSource(description);
    }

    /// <summary>Lists a painted source's declared resources once each, in declaration order.</summary>
    /// <remarks>
    /// The list this de-duplicates carries a drawable's declared bindings plus its fill and pen, so it is at
    /// most a handful of entries and a scan settles membership for less than a set costs to build. This runs
    /// once per painted primitive per recording.
    /// </remarks>
    private static RenderResource[] DistinctResources(RenderResourceBinding[] bindings)
    {
        var distinct = new RenderResource[bindings.Length];
        int count = 0;
        for (int index = 0; index < bindings.Length; index++)
        {
            RenderResource resource = bindings[index].Resource;
            bool seen = false;
            for (int other = 0; other < count; other++)
            {
                if (ReferenceEquals(distinct[other], resource))
                {
                    seen = true;
                    break;
                }
            }

            if (!seen)
                distinct[count++] = resource;
        }

        return count == distinct.Length ? distinct : distinct[..count];
    }

    private static bool ContainsDrawableBrush(Brush.Resource? fill, Pen.Resource? pen)
        => ContainsDrawableBrush(fill) || ContainsDrawableBrush(pen?.Brush);

    private static bool ContainsDrawableBrush(Brush.Resource? brush)
    {
        // A presenter chain is normally absent and never long, so the cycle guard is built only once one
        // is actually being walked - this runs once per fill and once per pen on every recording.
        HashSet<Brush.Resource>? visited = null;
        while (brush is BrushPresenter.Resource presenter)
        {
            visited ??= new HashSet<Brush.Resource>(ReferenceEqualityComparer.Instance);
            if (!visited.Add(brush))
                return true;

            brush = presenter.Target;
        }

        return brush is DrawableBrush.Resource;
    }

    /// <summary>Records an opaque value source whose callback runs only during execution.</summary>
    /// <param name="description">
    /// A non-null caller-owned source-topology description whose declared resources belong to this request family.
    /// </param>
    /// <returns>A new transaction-scoped source fragment. The result is not published automatically.</returns>
    public RenderFragmentHandle OpaqueSource(OpaqueRenderDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        description.ThrowIfIncompatible(OpaqueRenderTopology.Source, nameof(description));
        IReadOnlyList<RenderInputReadback> inputReadbacks = description.ResolveInputReadbacks(
            inputCount: 0,
            parameterName: nameof(description));
        ValidateDescriptionResources(description.Resources, nameof(description));

        Rect bounds = description.Bounds.TransformBounds([]);
        EffectiveScale scale = description.Scale.Resolve([], bounds, OutputScale, MaxWorkingScale);
        return GetTransaction().CreateFragment(
            RenderFragmentKind.OpaqueSource,
            bounds,
            scale,
            description.ValueCardinality,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: !description.HasDirectReplayMaterializationContract,
            inputs: [],
            new OpaqueRenderFragmentPayload(OpaqueRenderTopology.Source, description, inputReadbacks),
            RenderFragmentHitTest.FromContract(description.HitTest, description.Resources));
    }

    /// <summary>Records an opaque one-input value transformation.</summary>
    /// <param name="input">A non-null value-eligible fragment borrowed from the active transaction.</param>
    /// <param name="description">
    /// A non-null caller-owned map-topology description whose declared resources belong to this request family.
    /// </param>
    /// <returns>A new transaction-scoped opaque fragment. The result is not published automatically.</returns>
    public RenderFragmentHandle OpaqueMap(
        RenderFragmentHandle input,
        OpaqueRenderDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        EnsureValueInput(reference, nameof(input));
        description.ThrowIfIncompatible(OpaqueRenderTopology.Map, nameof(description));
        IReadOnlyList<RenderInputReadback> inputReadbacks = description.ResolveInputReadbacks(
            inputCount: 1,
            parameterName: nameof(description));
        ValidateDescriptionResources(description.Resources, nameof(description));

        Rect bounds = description.Bounds.TransformBounds([reference.Bounds]);
        EffectiveScale scale = description.Scale.Resolve(
            [reference.EffectiveScale],
            bounds,
            OutputScale,
            MaxWorkingScale);
        RenderValueCardinality cardinality = description.ValueCardinality.Equals(RenderValueCardinality.Single)
            ? reference.ValueCardinality
            : RenderValueCardinality.Range(0, reference.ValueCardinality.Maximum);
        return transaction.CreateFragment(
            RenderFragmentKind.OpaqueMap,
            bounds,
            scale,
            cardinality,
            reference.ContributesValuesToTarget,
            canBeUsedAsValueInput: true,
            hasTargetEffects: reference.HasTargetEffects,
            hasOpaqueExternalWork: true,
            [reference],
            new OpaqueRenderFragmentPayload(OpaqueRenderTopology.Map, description, inputReadbacks),
            RenderFragmentHitTest.FromContract(description.HitTest, description.Resources));
    }

    /// <summary>Records an opaque many-input combination.</summary>
    /// <param name="inputs">
    /// A non-null ordered list of non-null value-eligible fragments borrowed from the active transaction.
    /// </param>
    /// <param name="description">
    /// A non-null caller-owned combine-topology description whose declared resources belong to this request family.
    /// </param>
    /// <returns>A new transaction-scoped opaque fragment. The result is not published automatically.</returns>
    public RenderFragmentHandle OpaqueCombine(
        IReadOnlyList<RenderFragmentHandle> inputs,
        OpaqueRenderDescription description)
        => RecordOpaqueMany(inputs, description, OpaqueRenderTopology.Combine);

    /// <summary>Records an opaque many-input fragment that may expand value cardinality.</summary>
    /// <param name="inputs">
    /// A non-null ordered list of non-null value-eligible fragments borrowed from the active transaction.
    /// </param>
    /// <param name="description">
    /// A non-null caller-owned expand-topology description whose declared resources belong to this request family.
    /// </param>
    /// <returns>A new transaction-scoped opaque fragment. The result is not published automatically.</returns>
    public RenderFragmentHandle OpaqueExpand(
        IReadOnlyList<RenderFragmentHandle> inputs,
        OpaqueRenderDescription description)
        => RecordOpaqueMany(inputs, description, OpaqueRenderTopology.Expand);

    internal RenderFragmentHandle FilterEffectSegment(
        IReadOnlyList<RenderFragmentHandle> inputs,
        RenderResource<FilterEffectContext> effectContext,
        Rect outputBounds,
        bool requiresOwningTargetDomain = false,
        IReadOnlyList<IFEItem>? boundsItems = null,
        FilterEffectWorkingScalePolicy? workingScalePolicy = null)
    {
        ArgumentNullException.ThrowIfNull(effectContext);
        NodeRecordingTransaction transaction = GetTransaction();
        ImmutableArray<RenderFragmentReference> references =
            transaction.GetReferences(inputs, nameof(inputs));
        foreach (RenderFragmentReference reference in references)
            EnsureValueInput(reference, nameof(inputs));
        ValidateDescriptionResources([effectContext], nameof(effectContext));

        RenderRectValidation.ThrowIfInvalidInput(outputBounds, nameof(effectContext));
        IReadOnlyList<IFEItem> recordedBoundsItems = boundsItems ?? [];
        Rect[] bufferBounds = FilterEffectWorkingScalePolicy.CalculateEffectItemBufferBounds(
            references.SelectToArray(static item => item.Bounds),
            recordedBoundsItems,
            outputBounds);
        EffectiveScale scale;
        if (workingScalePolicy is { } policy)
        {
            scale = policy.Resolve(
                references.SelectToArray(static item => item.EffectiveScale),
                references.SelectToArray(static item => item.Bounds),
                bufferBounds,
                OutputScale,
                MaxWorkingScale);
        }
        else
        {
            scale = FilterEffectWorkingScalePolicy.ResolveMaterialized(
                references.SelectToArray(static item => item.EffectiveScale),
                bufferBounds,
                OutputScale,
                MaxWorkingScale);
        }

        RenderValueCardinality cardinality = ResolveFilterEffectSegmentCardinality(
            references,
            recordedBoundsItems,
            outputBounds,
            requiresOwningTargetDomain);

        return transaction.CreateFragment(
            RenderFragmentKind.FilterEffectSegment,
            outputBounds,
            scale,
            cardinality,
            references.Any(static item => item.ContributesValuesToTarget),
            canBeUsedAsValueInput: true,
            references.Any(static item => item.HasTargetEffects),
            hasOpaqueExternalWork: true,
            references,
            new FilterEffectSegmentRenderFragmentPayload(
                effectContext,
                [.. recordedBoundsItems],
                workingScalePolicy,
                references.Length),
            RenderFragmentHitTest.Bounds,
            requiresOwningTargetDomain
                ? RenderFragmentBoundsRequirement.OwningTargetDomain
                : RenderFragmentBoundsRequirement.Finite);
    }

    /// <summary>Records a declared render target as an existing materialized value without copying it.</summary>
    /// <param name="description">
    /// The non-null immutable target, bounds, concrete density, and hit-test contract. The target resource must
    /// belong to this request family; its resource registration determines disposal ownership.
    /// </param>
    /// <returns>A new transaction-scoped materialized input. The result is not published automatically.</returns>
    public RenderFragmentHandle MaterializedInput(MaterializedInputDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        ValidateDescriptionResources([description.Target], nameof(description));
        ValidateDescriptionResources(description.Resources, nameof(description));
        return GetTransaction().CreateFragment(
            RenderFragmentKind.MaterializedInput,
            description.Bounds,
            description.EffectiveScale,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            inputs: [],
            new MaterializedInputRenderFragmentPayload(description),
            RenderFragmentHitTest.FromContract(description.HitTest, description.Resources));
    }

    private RenderFragmentHandle RecordOpaqueMany(
        IReadOnlyList<RenderFragmentHandle> inputs,
        OpaqueRenderDescription description,
        OpaqueRenderTopology topology)
    {
        ArgumentNullException.ThrowIfNull(description);
        NodeRecordingTransaction transaction = GetTransaction();
        ImmutableArray<RenderFragmentReference> references =
            transaction.GetReferences(inputs, nameof(inputs));
        foreach (RenderFragmentReference reference in references)
            EnsureValueInput(reference, nameof(inputs));

        description.ThrowIfIncompatible(topology, nameof(description));
        IReadOnlyList<RenderInputReadback> inputReadbacks = description.ResolveInputReadbacks(
            references.Length,
            nameof(description));
        ValidateDescriptionResources(description.Resources, nameof(description));
        Rect bounds = description.Bounds.TransformBounds(
            references.SelectToArray(static item => item.Bounds));
        EffectiveScale scale = description.Scale.Resolve(
            references.SelectToArray(static item => item.EffectiveScale),
            bounds,
            OutputScale,
            MaxWorkingScale);
        return transaction.CreateFragment(
            topology == OpaqueRenderTopology.Combine
                ? RenderFragmentKind.OpaqueCombine
                : RenderFragmentKind.OpaqueExpand,
            bounds,
            scale,
            description.ValueCardinality,
            references.Any(static item => item.ContributesValuesToTarget),
            canBeUsedAsValueInput: true,
            hasTargetEffects: references.Any(static item => item.HasTargetEffects),
            hasOpaqueExternalWork: true,
            references,
            new OpaqueRenderFragmentPayload(topology, description, inputReadbacks),
            RenderFragmentHitTest.FromContract(description.HitTest, description.Resources));
    }

    private static RenderValueCardinality ResolveFilterEffectSegmentCardinality(
        IReadOnlyList<RenderFragmentReference> inputs,
        IReadOnlyList<IFEItem> items,
        Rect outputBounds,
        bool requiresOwningTargetDomain)
    {
        if (items.Count == 0 || items.Any(static item => item is not IFEItem_Skia))
            return RenderValueCardinality.Dynamic;

        RenderValueCardinality inputCardinality = AggregateCardinality(inputs);
        if (inputCardinality.Equals(RenderValueCardinality.Single))
        {
            bool outputMayBeEmpty = requiresOwningTargetDomain
                                    || outputBounds.Width == 0
                                    || outputBounds.Height == 0
                                    || items.Any(static item =>
                                        item is IFEItem_Skia { ResolveBoundsAtExecutionTime: true });
            return outputMayBeEmpty
                ? RenderValueCardinality.ZeroOrOne
                : RenderValueCardinality.Single;
        }

        return inputCardinality.Equals(RenderValueCardinality.ZeroOrOne)
            ? RenderValueCardinality.ZeroOrOne
            : RenderValueCardinality.Dynamic;
    }

    private sealed class PlainPaintedSource<TState>(
        TState state,
        PaintedSourceDraw<TState> draw,
        Brush.Resource? fill,
        Pen.Resource? pen,
        IReadOnlyList<RenderResource> declaredResources)
    {
        public void Execute(OpaqueRenderSession session)
        {
            using OpaqueRenderOutput output = session.CreateOutput(session.RequiredRegion);
            output.Canvas.Use(canvas => Draw(session.Token, canvas));
            session.Publish(output);
        }

        public void ExecuteDirect(EngineDirectRenderSession session)
            => Draw(session.Token, session.Canvas);

        private void Draw(RenderExecutionSessionToken token, ImmediateCanvas canvas)
            => token.UseResources(
                declaredResources,
                () => draw(canvas, fill, pen, state));
    }
}
