using System.Collections.Immutable;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Paints one source fragment's content onto the canvas the engine hands it during execution.
/// </summary>
/// <typeparam name="TState">The type of the caller-supplied state the callback paints from.</typeparam>
/// <param name="canvas">
/// The canvas the fragment paints into. It is already positioned so that the coordinates the node used to
/// declare its output bounds are the coordinates the callback draws in.
/// </param>
/// <param name="fill">The resolved fill brush, or <see langword="null"/> when the source paints no interior.</param>
/// <param name="pen">The resolved stroke pen, or <see langword="null"/> when the source paints no outline.</param>
/// <param name="state">
/// The state the node handed to <see cref="RenderNodeContext"/>'s <c>PaintedSource</c>.
/// </param>
/// <remarks>
/// The callback runs during execution, long after <see cref="RenderNode.Process(RenderNodeContext)"/> returned, so
/// it must not capture the recording context or any handle obtained from it. Declare it as a static lambda over the
/// four parameters. The callback reaches execution through the state channel, so capturing does not change the
/// description's identity - which is exactly why it is unsafe: a captured per-frame value shapes pixels without
/// <see cref="RenderNode.MarkChanged"/> observing it, so the node reports itself clean while its output is stale.
/// BESG003 rejects a capturing callback here.
/// </remarks>
public delegate void PaintedSourceDraw<TState>(
    ImmediateCanvas canvas,
    Brush.Resource? fill,
    Pen.Resource? pen,
    TState state);

/// <summary>
/// Records declarative render fragments for one active <see cref="RenderNode.Process(RenderNodeContext)"/> call.
/// </summary>
/// <remarks>
/// The engine creates and seals each transaction. Methods record metadata only; deferred callbacks run later.
/// The context, its borrowed <see cref="Inputs"/>, and all handles obtained from it become invalid when the
/// process call returns. They do not own rendering resources and cannot be retained for a later request.
/// </remarks>
public sealed class RenderNodeContext
{
    private readonly NodeRecordingTransaction _transaction;
    private readonly RenderFragmentHandle[] _inputs;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly Rect? _targetDomain;
    private readonly float _outputScale;
    private readonly float _maxWorkingScale;

    internal RenderNodeContext(NodeRecordingTransaction transaction)
    {
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _inputs = transaction.InputHandles;
        _intent = transaction.Request.Options.Intent;
        _purpose = transaction.Request.Options.Purpose;
        _targetDomain = transaction.Request.Options.TargetDomain;
        _outputScale = transaction.Request.Options.OutputScale;
        _maxWorkingScale = transaction.Request.Options.MaxWorkingScale;
    }

    /// <summary>Gets the non-null ordered fragment inputs borrowed by the current node transaction.</summary>
    public IReadOnlyList<RenderFragmentHandle> Inputs
    {
        get { VerifyActive(); return _inputs; }
    }

    /// <summary>Gets the render intent of the current request.</summary>
    public RenderIntent Intent
    {
        get { VerifyActive(); return _intent; }
    }

    /// <summary>Gets the purpose of the current request.</summary>
    public RenderRequestPurpose Purpose
    {
        get { VerifyActive(); return _purpose; }
    }

    /// <summary>Gets the optional finite logical domain available to root target accesses.</summary>
    public Rect? TargetDomain
    {
        get { VerifyActive(); return _targetDomain; }
    }

    /// <summary>Gets whether the current transaction remains eligible for persistent render caching.</summary>
    public bool IsRenderCacheEnabled
    {
        get { VerifyActive(); return _transaction.IsRenderCacheEnabled; }
    }

    /// <summary>
    /// Gets the positive finite final output density in device pixels per root logical unit.
    /// </summary>
    /// <remarks>This is informational for intermediate values and does not clamp their working density.</remarks>
    public float OutputScale
    {
        get { VerifyActive(); return _outputScale; }
    }

    /// <summary>
    /// Gets the sanitized request-wide ceiling for intermediate working densities.
    /// </summary>
    /// <remarks>The value is positive finite or positive infinity.</remarks>
    public float MaxWorkingScale
    {
        get { VerifyActive(); return _maxWorkingScale; }
    }

    /// <summary>Tries to calculate the union of all current input bounds from concrete recording metadata.</summary>
    /// <param name="bounds">
    /// Receives the logical input-bounds union, or <see langword="default"/> when any input still depends on an
    /// unresolved owning target domain. An empty input list succeeds with an empty rectangle.
    /// </param>
    /// <returns><see langword="true"/> when every input has concrete recording metadata.</returns>
    /// <remarks>This method does not execute deferred work or resolve graph-wide regions of interest.</remarks>
    public bool TryCalculateInputBounds(out Rect bounds)
    {
        VerifyActive();
        Rect result = default;
        for (int index = 0; index < _inputs.Length; index++)
        {
            RenderFragmentReference reference = _transaction.GetReference(_inputs[index]);
            if (!reference.HasConcreteRecordingMetadata)
            {
                bounds = default;
                return false;
            }

            result = result.Union(reference.RecordedBounds);
        }

        bounds = result;
        return true;
    }

    /// <summary>
    /// Attempts to compute the finite target domain that covers everything the current inputs put on the target.
    /// </summary>
    /// <param name="domain">
    /// When this method returns <see langword="true"/>, the union of every value-contributing input's recorded
    /// bounds with the resolved bounds of every target write the inputs perform; otherwise
    /// <see cref="Rect.Empty"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every value-contributing input has concrete recording metadata and every
    /// input's target write resolves to a finite region; <see langword="false"/> when any of them is still
    /// symbolic.
    /// </returns>
    /// <remarks>
    /// Valid only during <see cref="RenderNode.Process(RenderNodeContext)"/>, on the recording context passed to
    /// that call. A node that isolates its inputs into an off-screen layer uses this to choose between the finite
    /// <see cref="Layer(IReadOnlyList{RenderFragmentHandle}, Rect, bool)"/> and
    /// <see cref="OwningTargetLayer(IReadOnlyList{RenderFragmentHandle})"/>, whose domain is instead resolved from
    /// the enclosing target after recording. Unlike <see cref="TryCalculateInputBounds"/>, this accounts for target
    /// writes, so an input that clears or paints target pixels still yields a finite domain whenever the surrounding
    /// scopes bound that write. A returned domain of zero width or height means the inputs cover nothing, and the
    /// node can pass through instead of isolating.
    /// </remarks>
    public bool TryCalculateFiniteIsolationDomain(out Rect domain)
    {
        VerifyActive();
        Rect result = default;
        for (int index = 0; index < _inputs.Length; index++)
        {
            RenderFragmentReference reference = _transaction.GetReference(_inputs[index]);
            if (reference.ContributesValuesToTarget)
            {
                if (!reference.HasConcreteRecordingMetadata)
                {
                    domain = default;
                    return false;
                }

                result = result.Union(reference.RecordedBounds);
            }

            if (!TargetWriteMetadataResolver.TryResolveFinite(reference, out Rect? affectedBounds))
            {
                domain = default;
                return false;
            }

            if (affectedBounds is { } affected)
                result = result.Union(affected);
        }

        domain = result;
        return true;
    }

    /// <summary>Monotonically disables persistent render caching for the current node transaction.</summary>
    /// <remarks>
    /// A node that records a child it does not list in <see cref="RenderNode.ChildNodes"/> must call this,
    /// because the cache cannot observe a change reported only by that unlisted child.
    /// </remarks>
    public void DisableRenderCache()
    {
        GetTransaction().DisableRenderCache();
    }

    /// <summary>Publishes every current input unchanged and in order.</summary>
    public void PassThrough() => GetTransaction().PassThrough();

    /// <summary>Publishes one recorded fragment stream as a node output.</summary>
    /// <param name="fragment">A non-null handle borrowed from the active transaction.</param>
    public void Publish(RenderFragmentHandle fragment)
        => GetTransaction().Publish(fragment);

    /// <summary>Abandons a recorded fragment so it is neither published nor executed.</summary>
    /// <remarks>Required for a target-effect fragment recorded only to inspect its metadata.</remarks>
    /// <param name="fragment">A non-null unpublished handle borrowed from the active transaction.</param>
    public void Drop(RenderFragmentHandle fragment)
        => GetTransaction().Drop(fragment);

    /// <summary>Publishes recorded fragment streams in enumeration order.</summary>
    /// <param name="fragments">A non-null sequence of non-null handles borrowed from the active transaction.</param>
    public void PublishRange(IEnumerable<RenderFragmentHandle> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        NodeRecordingTransaction transaction = GetTransaction();
        foreach (RenderFragmentHandle fragment in fragments)
        {
            transaction.Publish(fragment);
        }
    }

    /// <summary>Maps every current input to one output and publishes the mapped outputs in input order.</summary>
    /// <param name="mapper">
    /// A synchronous callback that returns one active, unpublished handle for each borrowed input without
    /// publishing fragments itself.
    /// </param>
    /// <remarks>
    /// This is explicit publication for a one-to-one input transform. An empty input list invokes no callbacks and
    /// publishes no output. Use <see cref="Publish"/>, <see cref="PublishRange"/>, or <see cref="PassThrough"/>
    /// directly for other topologies or publication orders.
    /// </remarks>
    public void PublishMappedInputs(Func<RenderFragmentHandle, RenderFragmentHandle> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        PublishMappedInputs(mapper, static (_, input, callback) => callback(input));
    }

    /// <summary>Maps every current input to one output and publishes the mapped outputs in input order.</summary>
    /// <typeparam name="TState">The callback state supplied for every input.</typeparam>
    /// <param name="state">The callback state supplied for every input.</param>
    /// <param name="mapper">
    /// A synchronous callback that returns one active, unpublished handle for each borrowed input without
    /// publishing fragments itself.
    /// </param>
    /// <remarks>
    /// Pass explicit state with a <see langword="static"/> callback when the recording path must avoid a
    /// per-call capture. The context and every input handle remain transaction-scoped and must not be retained.
    /// </remarks>
    public void PublishMappedInputs<TState>(
        TState state,
        Func<RenderNodeContext, RenderFragmentHandle, TState, RenderFragmentHandle> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        NodeRecordingTransaction transaction = GetTransaction();
        foreach (RenderFragmentHandle input in _inputs)
        {
            int publicationCount = transaction.PublicationCount;
            RenderFragmentHandle mapped = mapper(this, input, state);
            if (transaction.PublicationCount != publicationCount)
            {
                throw new InvalidOperationException(
                    "A PublishMappedInputs mapper must return its output without publishing fragments.");
            }

            transaction.Publish(mapped);
        }
    }

    /// <summary>Wraps a value-eligible fragment so its values contribute to target composition when published.</summary>
    /// <param name="input">
    /// A non-null transaction-scoped fragment whose <see cref="RenderFragmentHandle.CanBeUsedAsValueInput"/> is
    /// <see langword="true"/>.
    /// </param>
    /// <returns>
    /// The borrowed original handle when it already contributes; otherwise a new transaction-scoped contributing
    /// handle. The result is not published automatically.
    /// </returns>
    public RenderFragmentHandle ContributeValues(RenderFragmentHandle input)
    {
        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        EnsureValueInput(reference, nameof(input));
        if (reference.ContributesValuesToTarget)
            return input;

        return transaction.CreateFragment(
            RenderFragmentKind.ContributeValues,
            reference.Bounds,
            reference.EffectiveScale,
            reference.ValueCardinality,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            reference.HasTargetEffects,
            reference.HasOpaqueExternalWork,
            [reference],
            payload: null,
            RenderFragmentHitTest.Inputs);
    }

    /// <summary>Records a deferred premultiplied-opacity scope around one fragment stream.</summary>
    /// <param name="input">A non-null fragment borrowed from the active transaction.</param>
    /// <param name="opacity">A finite opacity value. Values outside [0, 1] are clamped.</param>
    /// <returns>A new transaction-scoped fragment handle. The result is not published automatically.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="opacity"/> is not finite.</exception>
    public RenderFragmentHandle Opacity(RenderFragmentHandle input, float opacity)
    {
        opacity = OpacityRenderNode.Normalize(opacity);

        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        return transaction.CreateFragment(
            RenderFragmentKind.Opacity,
            reference.Bounds,
            reference.EffectiveScale,
            reference.ValueCardinality,
            reference.ContributesValuesToTarget,
            reference.CanBeUsedAsValueInput,
            reference.HasTargetEffects,
            reference.HasOpaqueExternalWork,
            [reference],
            new OpacityRenderFragmentPayload(
                opacity,
                OpacityRenderNode.CreateFusionDescription(opacity)),
            RenderFragmentHitTest.Inputs);
    }

    /// <summary>Records a blend-mode boundary around one input.</summary>
    /// <param name="input">A non-null fragment borrowed from the active transaction.</param>
    /// <param name="blendMode">The blend mode applied during target composition.</param>
    /// <returns>A new transaction-scoped blend fragment. The result is not published automatically.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="blendMode"/> is not a defined <see cref="BlendMode"/> value.
    /// </exception>
    public RenderFragmentHandle Blend(RenderFragmentHandle input, BlendMode blendMode)
    {
        if (!Enum.IsDefined(blendMode))
            throw new ArgumentOutOfRangeException(nameof(blendMode), blendMode, "The blend mode is not defined.");

        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        return transaction.CreateFragment(
            RenderFragmentKind.Blend,
            reference.Bounds,
            reference.EffectiveScale,
            reference.ValueCardinality,
            reference.ContributesValuesToTarget,
            canBeUsedAsValueInput: false,
            hasTargetEffects: true,
            reference.HasOpaqueExternalWork,
            [reference],
            new BlendRenderFragmentPayload(blendMode),
            RenderFragmentHitTest.Inputs);
    }

    /// <summary>Records an opacity-mask fragment and its declarative brush dependencies.</summary>
    /// <param name="input">A non-null fragment borrowed from the active transaction.</param>
    /// <param name="mask">
    /// The non-null mask resource whose scalar state and declared dependencies are captured during recording.
    /// </param>
    /// <param name="brushBounds">The finite logical coordinate frame used to map the mask brush.</param>
    /// <param name="invert">Whether to invert the sampled mask alpha.</param>
    /// <returns>A new transaction-scoped mask fragment. The result is not published automatically.</returns>
    public RenderFragmentHandle OpacityMask(
        RenderFragmentHandle input,
        RenderResource<Brush.Resource> mask,
        Rect brushBounds,
        bool invert = false)
    {
        ArgumentNullException.ThrowIfNull(mask);
        RenderRectValidation.ThrowIfInvalidInput(brushBounds, nameof(brushBounds));
        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        RenderDescriptionValidation.ThrowIfUndeclarable(mask, nameof(mask));
        return transaction.CreateFragment(
            RenderFragmentKind.OpacityMask,
            reference.Bounds,
            reference.EffectiveScale,
            reference.ValueCardinality,
            reference.ContributesValuesToTarget,
            reference.CanBeUsedAsValueInput,
            reference.HasTargetEffects,
            reference.HasOpaqueExternalWork,
            [reference],
            new OpacityMaskRenderFragmentPayload(
                mask,
                brushBounds,
                invert),
            RenderFragmentHitTest.Inputs);
    }

    /// <summary>Records a deferred shader transformation over one value-eligible fragment.</summary>
    /// <param name="input">
    /// A non-null transaction-scoped fragment whose <see cref="RenderFragmentHandle.CanBeUsedAsValueInput"/> is
    /// <see langword="true"/>.
    /// </param>
    /// <param name="description">
    /// The non-null caller-owned immutable shader contract. Every declared resource must belong to this request
    /// family.
    /// </param>
    /// <returns>A new transaction-scoped shader fragment. The result is not published automatically.</returns>
    public RenderFragmentHandle Shader(
        RenderFragmentHandle input,
        ShaderDescription description)
        => Shader(input, description, workingScalePolicy: null);

    internal RenderFragmentHandle Shader(
        RenderFragmentHandle input,
        ShaderDescription description,
        FilterEffectWorkingScalePolicy? workingScalePolicy)
    {
        ArgumentNullException.ThrowIfNull(description);
        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        EnsureValueInput(reference, nameof(input));
        ValidateDescriptionResources(
            description.Resources.SelectToArray(static binding => binding.Resource),
            nameof(description));

        Rect bounds = description.Bounds.TransformBounds(reference.Bounds);
        bool materializes = description.Kind == ShaderDescriptionKind.WholeSource;
        EffectiveScale scale;
        if (workingScalePolicy is { } policy)
        {
            scale = policy.Resolve(
                [reference],
                bounds,
                OutputScale,
                MaxWorkingScale);
        }
        else if (materializes)
        {
            float workingScale = RenderScaleUtilities.ResolveWorkingScale(
                [reference.EffectiveScale],
                OutputScale,
                MaxWorkingScale);
            workingScale = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(bounds, workingScale);
            scale = EffectiveScale.At(workingScale);
        }
        else
        {
            scale = reference.EffectiveScale;
        }

        return transaction.CreateFragment(
            RenderFragmentKind.Shader,
            bounds,
            scale,
            reference.ValueCardinality,
            reference.ContributesValuesToTarget,
            canBeUsedAsValueInput: true,
            reference.HasTargetEffects,
            reference.HasOpaqueExternalWork,
            [reference],
            new ShaderRenderFragmentPayload(
                description,
                workingScalePolicy),
            description.CreateFragmentHitTest());
    }

    /// <summary>Records a deferred geometry callback over one value-eligible fragment.</summary>
    /// <param name="input">
    /// A non-null transaction-scoped fragment whose <see cref="RenderFragmentHandle.CanBeUsedAsValueInput"/> is
    /// <see langword="true"/>.
    /// </param>
    /// <param name="description">
    /// The non-null caller-owned immutable geometry contract. Every declared resource must belong to this request
    /// family.
    /// </param>
    /// <returns>A new transaction-scoped geometry fragment. The result is not published automatically.</returns>
    public RenderFragmentHandle Geometry(
        RenderFragmentHandle input,
        GeometryDescription description)
        => Geometry(input, description, workingScalePolicy: null);

    internal RenderFragmentHandle Geometry(
        RenderFragmentHandle input,
        GeometryDescription description,
        FilterEffectWorkingScalePolicy? workingScalePolicy)
    {
        ArgumentNullException.ThrowIfNull(description);
        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        EnsureValueInput(reference, nameof(input));
        ValidateDescriptionResources(description.Resources, nameof(description));

        Rect bounds = description.Bounds.TransformBounds(reference.Bounds);
        EffectiveScale scale;
        if (workingScalePolicy is { } policy)
        {
            scale = policy.Resolve(
                [reference],
                bounds,
                OutputScale,
                MaxWorkingScale);
        }
        else
        {
            float workingScale = RenderScaleUtilities.ResolveWorkingScale(
                [reference.EffectiveScale],
                OutputScale,
                MaxWorkingScale);
            workingScale = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(bounds, workingScale);
            scale = EffectiveScale.At(workingScale);
        }

        RenderValueCardinality cardinality = RenderValueCardinality.Range(
            minimum: 0,
            maximum: reference.ValueCardinality.Maximum);
        return transaction.CreateFragment(
            RenderFragmentKind.Geometry,
            bounds,
            scale,
            cardinality,
            reference.ContributesValuesToTarget,
            canBeUsedAsValueInput: true,
            reference.HasTargetEffects,
            reference.HasOpaqueExternalWork,
            [reference],
            new GeometryRenderFragmentPayload(
                description,
                workingScalePolicy),
            RenderFragmentHitTest.FromContract(description.HitTest, description.Resources));
    }

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
    /// has no model of and cannot decide for itself, and a bare <paramref name="resources"/> list, whose tokens the
    /// engine binds to slots of its own - addressable by nothing, so no declared hit test can resolve one.
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
        IEnumerable<RenderResource>? resources = null,
        Thickness rasterOutset = default)
    {
        ArgumentNullException.ThrowIfNull(draw);
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(outputBounds, nameof(outputBounds));

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
            RenderDescriptionValidation.CopyResources(resources, nameof(resources))
                .Select(static resource => RenderResourceBinding.CreateEngineBinding(resource))
                .ToArray());
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
    /// <param name="slots">
    /// Declared slots. <paramref name="bindings"/> must bind each exactly once.
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
        IEnumerable<RenderResourceBinding>? bindings = null,
        IEnumerable<RenderResourceSlot>? slots = null,
        Thickness rasterOutset = default)
        where TState : notnull
    {
        ArgumentNullException.ThrowIfNull(draw);
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(outputBounds, nameof(outputBounds));

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
            RenderDescriptionValidation.BindDeclaredSlots(
                slots,
                bindings,
                nameof(slots),
                nameof(bindings)));
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

        var bindings = new List<RenderResourceBinding>(declaredBindings);
        if (fill is not null)
            bindings.Add(RenderResourceBinding.CreateEngineBinding(Borrow(fill)));
        if (pen is not null)
            bindings.Add(RenderResourceBinding.CreateEngineBinding(Borrow(pen)));

        var source = new PlainPaintedSource<TState>(
            state,
            draw,
            fill,
            pen,
            bindings
                .Select(static binding => binding.Resource)
                .DistinctBy(static resource => resource.SlotIdentity)
                .ToArray());
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

    private static bool ContainsDrawableBrush(Brush.Resource? fill, Pen.Resource? pen)
        => ContainsDrawableBrush(fill) || ContainsDrawableBrush(pen?.Brush);

    private static bool ContainsDrawableBrush(Brush.Resource? brush)
    {
        var visited = new HashSet<Brush.Resource>(ReferenceEqualityComparer.Instance);
        while (brush is BrushPresenter.Resource presenter)
        {
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

    /// <summary>Records a declared capture of the active target.</summary>
    /// <param name="description">The non-null immutable capture region, bounds, scale, and access contract.</param>
    /// <returns>
    /// A new transaction-scoped, non-contributing value fragment that contains the captured pixels when executed.
    /// The result is not published automatically.
    /// </returns>
    /// <remarks>The captured value is request-owned until it is released or transferred to an accepted cache.</remarks>
    public RenderFragmentHandle TargetCapture(TargetCaptureDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        ValidateDescriptionResources(description.Resources, nameof(description));
        EffectiveScale scale = description.Scale.PreservesTargetSupply
            ? EffectiveScale.Unbounded
            : description.Scale.ResolveDeclared(
                description.Bounds,
                OutputScale,
                MaxWorkingScale);
        return GetTransaction().CreateFragment(
            RenderFragmentKind.TargetCapture,
            description.Bounds,
            scale,
            RenderValueCardinality.Single,
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            inputs: [],
            new TargetCaptureRenderFragmentPayload(description),
            RenderFragmentHitTest.FromContract(description.HitTest, description.Resources));
    }

    internal RenderFragmentHandle BuiltInBackdropCapture(object identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity is not IBuiltInBackdropCaptureSink)
        {
            throw new ArgumentException(
                "A built-in backdrop capture identity must accept successful fallback publication.",
                nameof(identity));
        }
        NodeRecordingTransaction transaction = GetTransaction();
        var placeholder = new Rect(0, 0, 1, 1);
        var description = TargetCaptureDescription.Create(
            TargetRegion.Full,
            placeholder,
            RenderHitTestContract.None,
            TargetCaptureScaleContract.PreserveTargetSupply);
        RenderFragmentHandle handle = transaction.CreateFragment(
            RenderFragmentKind.BuiltInBackdropCapture,
            placeholder,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            inputs: [],
            new BuiltInBackdropCaptureRenderFragmentPayload(description, identity),
            RenderFragmentHitTest.FromContract(description.HitTest, description.Resources),
            boundsRequirement: RenderFragmentBoundsRequirement.OwningTargetDomain);
        transaction.BindBuiltInBackdrop(identity, handle);
        return handle;
    }

    internal bool TryBuiltInBackdrop(
        object identity,
        out RenderFragmentHandle? capture)
        => GetTransaction().TryGetBuiltInBackdrop(identity, out capture);

    /// <summary>Records a finite off-screen layer and returns its composited value.</summary>
    /// <param name="inputs">A non-null ordered list of non-null fragments replayed inside the layer.</param>
    /// <param name="domain">The finite logical layer domain.</param>
    /// <param name="domainIsQueryFootprint">
    /// <see langword="true"/> when the layer occupies its whole <paramref name="domain"/> for bounds queries even
    /// where it draws nothing — a fixed-size viewport such as a nested scene, whose layout footprint is the frame
    /// it references rather than its content. Output bounds, rasterization regions, and hit testing stay
    /// content-derived either way; only the queried footprint changes.
    /// </param>
    /// <returns>
    /// A new transaction-scoped single-value fragment. The result is not published automatically and owns no
    /// execution resource itself.
    /// </returns>
    /// <remarks>
    /// A finite Layer is a concrete-metadata barrier. If any input has symbolic recording metadata, the result uses
    /// the complete <paramref name="domain"/> for conservative bounds and hit testing.
    /// </remarks>
    public RenderFragmentHandle Layer(
        IReadOnlyList<RenderFragmentHandle> inputs,
        Rect domain,
        bool domainIsQueryFootprint = false)
    {
        if (!RenderRectValidation.IsFiniteNonNegative(domain)
            || domain.Width == 0
            || domain.Height == 0)
        {
            throw new ArgumentException("A finite Layer domain must be finite and non-empty.", nameof(domain));
        }

        NodeRecordingTransaction transaction = GetTransaction();
        ImmutableArray<RenderFragmentReference> references =
            transaction.GetReferences(inputs, nameof(inputs));
        bool hasConcreteInputMetadata = references.All(
            static reference => reference.HasConcreteRecordingMetadata);
        bool contributes = false;
        Rect bounds = default;
        foreach (RenderFragmentReference reference in references)
        {
            if (reference.ContributesValuesToTarget)
            {
                contributes = true;
                bounds = bounds.Union(reference.Bounds);
            }

            if (TargetWriteMetadataResolver.Resolve(reference, domain) is { } affected)
            {
                contributes = true;
                bounds = bounds.Union(affected);
            }
        }
        bounds = hasConcreteInputMetadata
            ? bounds.Intersect(domain)
            : domain;
        // The layer's own bounds are clipped to the domain above, so a point the domain excludes names
        // content the layer cannot render however far an input's geometry reaches.
        RenderFragmentHitTest hitTest = hasConcreteInputMetadata
            ? RenderFragmentHitTest.RegionAndInputs(domain)
            : RenderFragmentHitTest.Region(domain);
        return transaction.CreateFragment(
            RenderFragmentKind.Layer,
            bounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            contributes,
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: references.Any(static item => item.HasOpaqueExternalWork),
            references,
            new LayerRenderFragmentPayload(domain, domainIsQueryFootprint),
            hitTest);
    }

    /// <summary>
    /// Records an off-screen layer whose finite domain is resolved from its owning target after surrounding
    /// target scopes are known.
    /// </summary>
    /// <param name="inputs">A non-null ordered list of non-null fragments replayed inside the layer.</param>
    /// <returns>
    /// A new transaction-scoped single-value fragment. The result is not published automatically and remains
    /// symbolic until graph-wide target-domain resolution.
    /// </returns>
    /// <remarks>
    /// Use this form when a mixed painter sequence must become value-eligible but no finite domain is available
    /// during recording. Graph finalization rejects the fragment unless an enclosing scope or request supplies a
    /// finite owning target domain.
    /// </remarks>
    public RenderFragmentHandle OwningTargetLayer(
        IReadOnlyList<RenderFragmentHandle> inputs)
    {
        NodeRecordingTransaction transaction = GetTransaction();
        ImmutableArray<RenderFragmentReference> references =
            transaction.GetReferences(inputs, nameof(inputs));
        Rect recordedBounds = CalculateReferenceBounds(references);
        return transaction.CreateFragment(
            RenderFragmentKind.Layer,
            recordedBounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.Single,
            references.Any(static reference =>
                reference.ContributesValuesToTarget || reference.PotentiallyWritesTarget),
            canBeUsedAsValueInput: true,
            hasTargetEffects: true,
            hasOpaqueExternalWork: references.Any(static item => item.HasOpaqueExternalWork),
            references,
            new LayerRenderFragmentPayload(Domain: null),
            RenderFragmentHitTest.Inputs,
            boundsRequirement: RenderFragmentBoundsRequirement.OwningTargetDomain);
    }

    /// <summary>Records ordered target work scoped to a symbolic target region.</summary>
    /// <param name="inputs">A non-null ordered list of non-null fragments replayed inside the scope.</param>
    /// <param name="region">The target region resolved after surrounding domains are known.</param>
    /// <returns>
    /// A new transaction-scoped, non-value-eligible target-effect fragment. The result is not published
    /// automatically.
    /// </returns>
    public RenderFragmentHandle TargetLayerScope(
        IReadOnlyList<RenderFragmentHandle> inputs,
        TargetRegion region)
    {
        region.ThrowIfUninitialized(nameof(region));
        NodeRecordingTransaction transaction = GetTransaction();
        ImmutableArray<RenderFragmentReference> references =
            transaction.GetReferences(inputs, nameof(inputs));
        return transaction.CreateFragment(
            RenderFragmentKind.TargetLayerScope,
            CalculateReferenceBounds(references),
            EffectiveScale.Unbounded,
            AggregateCardinality(references),
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: false,
            hasTargetEffects: true,
            hasOpaqueExternalWork: references.Any(static item => item.HasOpaqueExternalWork),
            references,
            new TargetLayerScopeRenderFragmentPayload(region),
            CreateTargetLayerScopeHitTest(region),
            hasDirectSymbolicBoundsDependency: region.Kind == TargetRegionKind.Full);
    }

    // A finite region bounds what the scope can put on its target the same way it bounds rasterization, so a
    // point outside it names content this scope cannot render however far an input's geometry reaches. A Full
    // region has no recording-time extent to test against and defers to its inputs, and an Empty one renders
    // nothing at all.
    private static RenderFragmentHitTest CreateTargetLayerScopeHitTest(TargetRegion region)
        => region.Kind switch
        {
            TargetRegionKind.Empty => RenderFragmentHitTest.None,
            TargetRegionKind.Region => RenderFragmentHitTest.RegionAndInputs(region.Value),
            _ => RenderFragmentHitTest.Inputs,
        };

    /// <summary>Records a guarded target scope around one input.</summary>
    /// <param name="input">A non-null fragment borrowed from the active transaction and replayed inside the scope.</param>
    /// <param name="description">
    /// The non-null caller-owned guarded scope contract. Every declared resource must belong to this request family.
    /// </param>
    /// <returns>A new transaction-scoped target scope. The result is not published automatically.</returns>
    public RenderFragmentHandle TargetScope(
        RenderFragmentHandle input,
        TargetScopeDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        return RecordTargetScope(input, description, raw: false);
    }

    /// <summary>Records an opaque external target scope around one input.</summary>
    /// <param name="input">A non-null fragment borrowed from the active transaction and replayed inside the scope.</param>
    /// <param name="description">
    /// The non-null caller-owned raw scope contract. Every declared resource must belong to this request family.
    /// </param>
    /// <returns>A new transaction-scoped external-work boundary. The result is not published automatically.</returns>
    public RenderFragmentHandle RawTargetScope(
        RenderFragmentHandle input,
        RawTargetScopeDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        return RecordTargetScope(input, description, raw: true);
    }

    /// <summary>Records an opaque external command against the active target.</summary>
    /// <param name="description">
    /// The non-null caller-owned raw command contract. Every declared resource must belong to this request family.
    /// </param>
    /// <returns>A new transaction-scoped external-work boundary. The result is not published automatically.</returns>
    public RenderFragmentHandle RawTargetCommand(RawTargetCommandDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        ValidateDescriptionResources(description.Resources, nameof(description));
        return GetTransaction().CreateFragment(
            RenderFragmentKind.RawTargetCommand,
            description.QueryBounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.None,
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: false,
            hasTargetEffects: true,
            hasOpaqueExternalWork: true,
            inputs: [],
            new RawTargetCommandRenderFragmentPayload(description),
            RenderFragmentHitTest.FromContract(description.HitTest, description.Resources));
    }

    /// <summary>Records a guarded command that consumes declared values and accesses the active target.</summary>
    /// <param name="inputs">
    /// A non-null ordered list of non-null value-eligible fragments borrowed from the active transaction and made
    /// available to the command.
    /// </param>
    /// <param name="description">
    /// The non-null caller-owned guarded command contract. Every declared resource must belong to this request
    /// family.
    /// </param>
    /// <returns>A new transaction-scoped target command. The result is not published automatically.</returns>
    public RenderFragmentHandle TargetCommand(
        IReadOnlyList<RenderFragmentHandle> inputs,
        TargetCommandDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        NodeRecordingTransaction transaction = GetTransaction();
        ImmutableArray<RenderFragmentReference> references =
            transaction.GetReferences(inputs, nameof(inputs));
        foreach (RenderFragmentReference reference in references)
            EnsureValueInput(reference, nameof(inputs));
        IReadOnlyList<RenderInputReadback> inputReadbacks = description.ResolveInputReadbacks(
            references.Length,
            nameof(description));
        ValidateDescriptionResources(description.Resources, nameof(description));

        return transaction.CreateFragment(
            RenderFragmentKind.TargetCommand,
            description.QueryBounds,
            EffectiveScale.Unbounded,
            RenderValueCardinality.None,
            contributesValuesToTarget: false,
            canBeUsedAsValueInput: false,
            hasTargetEffects: true,
            hasOpaqueExternalWork: false,
            references,
            new TargetCommandRenderFragmentPayload(description, inputReadbacks),
            RenderFragmentHitTest.FromContract(description.HitTest, description.Resources));
    }

    /// <summary>Records a root and its descendants into the current request without executing them.</summary>
    /// <param name="root">The non-null caller-owned subtree root.</param>
    /// <returns>A non-null borrowed list of the subtree's transaction-scoped outputs.</returns>
    public IReadOnlyList<RenderFragmentHandle> RecordSubtree(RenderNode root)
        => GetTransaction().RecordNode(root, [], subtree: true);

    /// <summary>Records another node with explicit inputs into the current request.</summary>
    /// <param name="node">The non-null caller-owned node to record.</param>
    /// <param name="inputs">A non-null ordered list of non-null inputs remapped into the child transaction.</param>
    /// <returns>A non-null borrowed list of the child node's outputs remapped into this transaction.</returns>
    public IReadOnlyList<RenderFragmentHandle> RecordNode(
        RenderNode node,
        IReadOnlyList<RenderFragmentHandle> inputs)
        => GetTransaction().RecordNode(node, inputs, subtree: false);

    internal RecordedNestedRenderTarget RecordNestedTarget(
        RenderNode root,
        Rect targetDomain,
        Rect? requestedRegion = null)
        => RecordNestedTargetCore(
            root,
            targetDomain,
            requestedRegion,
            workingScale: null);

    internal RecordedNestedRenderTarget RecordNestedTargetAtScale(
        RenderNode root,
        Rect targetDomain,
        float workingScale,
        Rect? requestedRegion = null)
        => RecordNestedTargetCore(
            root,
            targetDomain,
            requestedRegion,
            workingScale);

    private RecordedNestedRenderTarget RecordNestedTargetCore(
        RenderNode root,
        Rect targetDomain,
        Rect? requestedRegion,
        float? workingScale)
    {
        ArgumentNullException.ThrowIfNull(root);
        var binding = new NestedRenderTargetBinding();
        RenderResource<NestedRenderTargetBinding>? bindingResource = null;
        NodeRecordingTransaction transaction = GetTransaction();
        try
        {
            bindingResource = transaction.Own(binding);
            RenderRequestOptions nestedOptions = workingScale is { } scale
                ? transaction.Request.Options.CreateNestedAtScale(
                    binding,
                    scale,
                    targetDomain,
                    requestedRegion ?? targetDomain)
                : transaction.Request.Options.CreateNested(
                    binding,
                    targetDomain,
                    requestedRegion ?? targetDomain);
            RecordedNestedRenderRequest recording = transaction.RecordNestedRequest(
                root,
                nestedOptions);
            return new RecordedNestedRenderTarget(recording, bindingResource, binding);
        }
        catch (Exception ex)
        {
            if (bindingResource is not null)
            {
                _ = transaction.RollbackResourcesAndCapture([bindingResource], ex);
            }
            else
            {
                transaction.Request.Options.Owner.RecordPrimaryFailure(ex);
                try
                {
                    binding.Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    transaction.Request.Options.Owner.RecordCleanupFailure(cleanupFailure);
                }
            }

            throw;
        }
    }

    /// <summary>Transfers a disposable resource to the current request family.</summary>
    /// <typeparam name="T">The disposable resource type.</typeparam>
    /// <param name="resource">The non-null resource whose ownership is transferred.</param>
    /// <returns>A non-null declared resource handle owned by the request family.</returns>
    /// <remarks>
    /// Ownership transfers when this method succeeds. The family disposes the resource exactly once on rollback,
    /// failure, or normal completion.
    /// </remarks>
    public RenderResource<T> Own<T>(T resource)
        where T : class, IDisposable
        => GetTransaction().Own(resource);

    /// <summary>Registers a caller-owned resource that the current request may borrow.</summary>
    /// <typeparam name="T">The resource type.</typeparam>
    /// <param name="resource">The non-null caller-owned resource.</param>
    /// <returns>A non-null declared resource handle that never transfers disposal ownership.</returns>
    /// <remarks>
    /// The request borrows the resource only for its active family and never disposes it. Resource registrations
    /// do not provide persistent render-cache identity; cache eligibility follows the node's change reporting.
    /// </remarks>
    public RenderResource<T> Borrow<T>(T resource)
        where T : class
        => GetTransaction().Borrow(resource);

    internal void RollbackResources(IReadOnlyList<RenderResource> resources)
        => GetTransaction().RollbackResources(resources);

    internal Exception? RollbackResourcesAndCapture(
        IReadOnlyList<RenderResource> resources,
        Exception primaryFailure)
        => GetTransaction().RollbackResourcesAndCapture(resources, primaryFailure);

    /// <summary>Reads the recording-time bounds and supply density already recorded for one fragment.</summary>
    /// <param name="fragment">A non-null handle borrowed from the active transaction.</param>
    /// <returns>The fragment's recorded bounds paired with the density at which it can supply values.</returns>
    /// <remarks>
    /// Valid only during <see cref="RenderNode.Process(RenderNodeContext)"/>, on the recording context passed to
    /// that call. This is a hint, not a guarantee: it reports what recording knows so far and never forces
    /// resolution, so a fragment whose metadata is still symbolic reports the conservative values recorded up to
    /// this point rather than its final ones. Use it to size or order work while recording; use
    /// <see cref="TryCalculateInputBounds"/> when the node must instead know whether concrete metadata exists at
    /// all.
    /// </remarks>
    public RenderFragmentMetadata GetRecordedMetadataHint(RenderFragmentHandle fragment)
    {
        RenderFragmentReference reference = GetTransaction().GetReference(fragment);
        return new RenderFragmentMetadata(reference.RecordedBounds, reference.RecordedEffectiveScale);
    }

    /// <summary>
    /// Attempts to compute the finite region that a set of already-recorded fragments covers on the target.
    /// </summary>
    /// <param name="fragments">
    /// A non-null list of handles borrowed from the active transaction. Unlike
    /// <see cref="TryCalculateFiniteIsolationDomain(out Rect)"/>, which always measures the node's own inputs, this
    /// measures whichever fragments the node names — typically ones it has just recorded itself.
    /// </param>
    /// <param name="extent">
    /// When this method returns <see langword="true"/>, the union of every value-producing fragment's recorded
    /// bounds with the resolved bounds of every target write those fragments perform; otherwise
    /// <see cref="Rect.Empty"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every value-producing fragment has concrete recording metadata and every target
    /// write resolves to a finite region; <see langword="false"/> when any of them is still symbolic.
    /// </returns>
    /// <remarks>
    /// Valid only during <see cref="RenderNode.Process(RenderNodeContext)"/>, on the recording context passed to
    /// that call. A node that has recorded a sub-graph and now has to size something around it — a clip, a layer, a
    /// backdrop read — uses this to learn whether that sub-graph's footprint is knowable at recording time. A
    /// returned extent of zero width or height means the fragments cover nothing. A <see langword="false"/> result
    /// is not an error: it says the footprint is only known once the enclosing target is resolved, so the node must
    /// fall back on a target-resolved construct such as
    /// <see cref="OwningTargetLayer(IReadOnlyList{RenderFragmentHandle})"/> instead of a finite rectangle.
    /// </remarks>
    public bool TryCalculateRecordedOutputExtent(
        IReadOnlyList<RenderFragmentHandle> fragments,
        out Rect extent)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        NodeRecordingTransaction transaction = GetTransaction();
        Rect result = default;
        foreach (RenderFragmentHandle fragment in fragments)
        {
            RenderFragmentReference reference = transaction.GetReference(fragment);
            if (reference.ValueCardinality.Maximum != 0)
            {
                if (!reference.HasConcreteRecordingMetadata)
                {
                    extent = default;
                    return false;
                }

                result = result.Union(reference.RecordedBounds);
            }

            if (!TargetWriteMetadataResolver.TryResolveFinite(reference, out Rect? affectedBounds))
            {
                extent = default;
                return false;
            }

            if (affectedBounds is { } affected)
                result = result.Union(affected);
        }

        extent = result;
        return true;
    }

    /// <summary>Unions the recorded bounds of every current input.</summary>
    /// <returns>
    /// The union of each input's recorded bounds, or <see cref="Rect.Empty"/> when the node has no inputs.
    /// </returns>
    /// <remarks>
    /// Valid only during <see cref="RenderNode.Process(RenderNodeContext)"/>, on the recording context passed to
    /// that call. This always returns a rectangle, where <see cref="TryCalculateInputBounds"/> reports failure for a
    /// symbolic input, so the result is a best-effort hint: it describes only recorded <em>value</em> bounds and
    /// therefore does not cover a full-target write. A node scoping its inputs by this rectangle must first ask
    /// <see cref="HasSymbolicInputTargetWrite"/> whether such a write exists.
    /// </remarks>
    public Rect CalculateRecordedInputBoundsHint()
    {
        NodeRecordingTransaction transaction = GetTransaction();
        Rect result = default;
        foreach (RenderFragmentHandle input in _inputs)
        {
            result = result.Union(transaction.GetReference(input).RecordedBounds);
        }

        return result;
    }

    /// <summary>
    /// Gets whether a recorded input writes target pixels that
    /// <see cref="CalculateRecordedInputBoundsHint"/> does not describe.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when at least one input performs a target write with no recording-time extent.
    /// </returns>
    /// <remarks>
    /// Valid only during <see cref="RenderNode.Process(RenderNodeContext)"/>, on the recording context passed to
    /// that call. This is the predicate that picks the region for
    /// <see cref="TargetLayerScope(IReadOnlyList{RenderFragmentHandle}, TargetRegion)"/>. A node that scopes its
    /// inputs by their recorded value bounds has to ask this first: a full-target write contributes no value
    /// bounds, so scoping by them alone turns the whole scope empty and drops the write. Answer
    /// <see langword="true"/> with <see cref="TargetRegion.Full"/> and <see langword="false"/> with
    /// <see cref="TargetRegion.Region(Rect)"/> over <see cref="CalculateRecordedInputBoundsHint"/>. Passing
    /// <see cref="TargetRegion.Full"/> unconditionally is not a safe shortcut — it makes bounds-dependent
    /// downstream work measure against the whole target.
    /// </remarks>
    public bool HasSymbolicInputTargetWrite()
    {
        NodeRecordingTransaction transaction = GetTransaction();
        foreach (RenderFragmentHandle input in _inputs)
        {
            if (transaction.GetReference(input).HasSymbolicTargetWrite)
                return true;
        }

        return false;
    }

    private NodeRecordingTransaction GetTransaction()
    {
        VerifyActive();
        return _transaction;
    }

    private void VerifyActive() => _transaction.VerifyActive();

    private static void EnsureValueInput(RenderFragmentReference reference, string parameterName)
    {
        if (!reference.CanBeUsedAsValueInput)
        {
            throw new ArgumentException(
                "The fragment cannot be consumed as a materialized value input. Use a finite Layer explicitly.",
                parameterName);
        }
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

    private RenderFragmentHandle RecordTargetScope(
        RenderFragmentHandle input,
        object description,
        bool raw)
    {
        NodeRecordingTransaction transaction = GetTransaction();
        RenderFragmentReference reference = transaction.GetReference(input);
        RenderBoundsContract boundsContract;
        RenderHitTestContract hitTestContract;
        RenderScaleContract scaleContract;
        IReadOnlyList<RenderResourceBinding> resourceBindings;
        if (description is TargetScopeDescription typed)
        {
            boundsContract = typed.Bounds;
            hitTestContract = typed.HitTest;
            scaleContract = typed.Scale;
            resourceBindings = typed.Resources;
        }
        else if (description is RawTargetScopeDescription rawDescription)
        {
            boundsContract = rawDescription.Bounds;
            hitTestContract = rawDescription.HitTest;
            scaleContract = rawDescription.Scale;
            resourceBindings = rawDescription.Resources;
        }
        else
        {
            throw new ArgumentException("The target scope description type is invalid.", nameof(description));
        }

        ValidateDescriptionResources(resourceBindings, nameof(description));
        Rect bounds = boundsContract.TransformBounds(reference.Bounds);
        EffectiveScale scale = scaleContract.Resolve(
            [reference.EffectiveScale],
            bounds,
            OutputScale,
            MaxWorkingScale);
        bool isValueReplayMap = !raw
            && ((TargetScopeDescription)description).IsValueReplayMap;
        return transaction.CreateFragment(
            raw ? RenderFragmentKind.RawTargetScope : RenderFragmentKind.TargetScope,
            bounds,
            scale,
            reference.ValueCardinality,
            reference.ContributesValuesToTarget,
            canBeUsedAsValueInput: isValueReplayMap
                && reference.CanBeUsedAsValueInput
                && reference.ValueCardinality.Equals(RenderValueCardinality.Single)
                && reference.ContributesValuesToTarget
                && !RenderFragmentTargetDependency.HasExternalTargetDependency(reference),
            hasTargetEffects: isValueReplayMap ? reference.HasTargetEffects : true,
            hasOpaqueExternalWork: raw || reference.HasOpaqueExternalWork,
            [reference],
            raw
                ? new RawTargetScopeRenderFragmentPayload((RawTargetScopeDescription)description)
                : new TargetScopeRenderFragmentPayload((TargetScopeDescription)description),
            RenderFragmentHitTest.FromContract(hitTestContract, resourceBindings));
    }

    private void ValidateDescriptionResources(
        IReadOnlyList<RenderResource> resources,
        string parameterName)
        => ValidateDeclaredResources(resources, static resource => resource, parameterName);

    private void ValidateDescriptionResources(
        IReadOnlyList<RenderResourceBinding> resources,
        string parameterName)
        => ValidateDeclaredResources(resources, static binding => binding.Resource, parameterName);

    /// <remarks>
    /// A description declares its resources either as bare tokens or as slot bindings, so the resource is
    /// read through a selector rather than by projecting one list into the other. Every recording path below
    /// reaches this once per operation per frame, and the projection was an array allocated per call to be
    /// read once and dropped.
    /// </remarks>
    private void ValidateDeclaredResources<TDeclared>(
        IReadOnlyList<TDeclared> declared,
        Func<TDeclared, RenderResource> selectResource,
        string parameterName)
    {
        NodeRecordingTransaction transaction = GetTransaction();
        for (int index = 0; index < declared.Count; index++)
        {
            RenderResource resource = selectResource(declared[index]);
            if (!ReferenceEquals(resource.Registry, transaction.Request.Options.Owner.ResourceRegistry)
                || resource.RegistrationState == RenderResourceRegistrationState.Released)
            {
                throw new ArgumentException(
                    "Every declared render resource must belong to the active request family.",
                    parameterName);
            }
        }
    }

    private static Rect CalculateReferenceBounds(
        IEnumerable<RenderFragmentReference> references)
    {
        Rect result = default;
        foreach (RenderFragmentReference reference in references)
        {
            result = result.Union(reference.Bounds);
        }

        return result;
    }

    private static RenderValueCardinality AggregateCardinality(
        IEnumerable<RenderFragmentReference> references)
    {
        int minimum = 0;
        int? maximum = 0;
        foreach (RenderFragmentReference reference in references)
        {
            minimum = checked(minimum + reference.ValueCardinality.Minimum);
            maximum = maximum is null || reference.ValueCardinality.Maximum is null
                ? null
                : checked(maximum.Value + reference.ValueCardinality.Maximum.Value);
        }

        return RenderValueCardinality.Range(minimum, maximum);
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


internal sealed record OpacityRenderFragmentPayload(
    float Opacity,
    ShaderDescription FusionDescription);

internal sealed record BlendRenderFragmentPayload(BlendMode BlendMode);

internal sealed record OpacityMaskRenderFragmentPayload(
    RenderResource<Brush.Resource> Mask,
    Rect BrushBounds,
    bool Invert);

internal sealed record ShaderRenderFragmentPayload(
    ShaderDescription Description,
    FilterEffectWorkingScalePolicy? WorkingScalePolicy = null);

internal sealed record GeometryRenderFragmentPayload(
    GeometryDescription Description,
    FilterEffectWorkingScalePolicy? WorkingScalePolicy = null);

internal sealed record LayerRenderFragmentPayload(Rect? Domain, bool DomainIsQueryFootprint = false);

internal sealed record TargetLayerScopeRenderFragmentPayload(TargetRegion Region);

internal sealed record OpaqueRenderFragmentPayload(
    OpaqueRenderTopology Topology,
    OpaqueRenderDescription Description,
    IReadOnlyList<RenderInputReadback> InputReadbacks);

internal sealed record FilterEffectSegmentRenderFragmentPayload(
    RenderResource<FilterEffectContext> Context,
    ImmutableArray<IFEItem> BoundsItems,
    FilterEffectWorkingScalePolicy? WorkingScalePolicy,
    int StreamInputCount)
{
    /// <summary>
    /// Whether the segment runs an imperative effect callback. Such a callback crops and re-lays-out its
    /// targets in whole device pixels, so the executor strips the sub-pixel phase from the ambient device
    /// grid for this segment and for every nested frame that materializes its inputs. Only the ambient
    /// phase is stripped: a callback whose own target bounds carry a fractional device phase still
    /// allocates off the grid, and an input produced by a separate render request keeps that request's
    /// own grid.
    /// </summary>
    public bool HasImperativeItem
    {
        get
        {
            if (BoundsItems.IsDefaultOrEmpty)
                return false;

            // ImmutableArray's own enumerator is a struct; Enumerable.Any would box it on a path the
            // executor walks for every effect-item-filter fragment it runs.
            foreach (IFEItem item in BoundsItems)
            {
                if (item is IFEItem_Custom)
                    return true;
            }

            return false;
        }
    }

    public bool SupportsDirectReplay
        => StreamInputCount == 1
           && !BoundsItems.IsDefaultOrEmpty
           && BoundsItems.All(static item =>
               item is IFEItem_Skia
               {
                   SupportsDirectReplay: true,
                   ResolveBoundsAtExecutionTime: false,
               });
}

internal static class FilterEffectSegmentDirectReplaySupport
{
    public static bool CanMaterialize(RenderFragmentReference fragment)
    {
        if (!fragment.ContributesValuesToTarget || !TryGetPayload(fragment, out _))
            return false;

        RenderFragmentReference input = fragment.Inputs[0];
        while (TryGetPayload(input, out _))
            input = input.Inputs[0];

        return input.ContributesValuesToTarget
               && input.ValueCardinality.Equals(RenderValueCardinality.Single);
    }

    private static bool TryGetPayload(
        RenderFragmentReference fragment,
        out FilterEffectSegmentRenderFragmentPayload payload)
    {
        if (fragment.Kind == RenderFragmentKind.FilterEffectSegment
            && fragment.Inputs.Length == 1
            && fragment.Payload is FilterEffectSegmentRenderFragmentPayload
            {
                SupportsDirectReplay: true,
            } directPayload)
        {
            payload = directPayload;
            return true;
        }

        payload = null!;
        return false;
    }
}

internal sealed record MaterializedInputRenderFragmentPayload(
    MaterializedInputDescription Description);

internal sealed record TargetCaptureRenderFragmentPayload(
    TargetCaptureDescription Description);

internal sealed record BuiltInBackdropCaptureRenderFragmentPayload(
    TargetCaptureDescription Description,
    object Identity);

internal sealed record TargetScopeRenderFragmentPayload(
    TargetScopeDescription Description);

internal sealed record RawTargetScopeRenderFragmentPayload(
    RawTargetScopeDescription Description);

internal sealed record RawTargetCommandRenderFragmentPayload(
    RawTargetCommandDescription Description);

internal sealed record TargetCommandRenderFragmentPayload(
    TargetCommandDescription Description,
    IReadOnlyList<RenderInputReadback> InputReadbacks);

internal interface IBuiltInBackdropCaptureSink
{
    bool TryCommitBackdropCapture(Bitmap bitmap, float density)
    {
        CommitBackdropCapture(bitmap, density);
        return true;
    }

    void CommitBackdropCapture(Bitmap bitmap, float density);
}
