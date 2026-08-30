using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Defines the fixed shape of an opaque render operation.
/// </summary>
/// <typeparam name="TState">The per-recording state supplied by an <see cref="OpaqueRenderCall{TState}"/>.</typeparam>
/// <remarks>
/// <para>
/// A definition contains only operation shape: its callback, metadata contracts, and planner traits. Values that
/// affect pixels belong to a call. When those values change, the owning <see cref="RenderNode"/> must call
/// <see cref="RenderNode.MarkChanged"/> before its next request.
/// </para>
/// <para>
/// The bounds contract is one of those metadata contracts, so what an operation declares about its geometry is
/// answered before any call supplies state. An operation whose geometry is itself a per-recording value builds
/// its definition inside <see cref="RenderNode.Process"/> rather than holding one; nothing is lost by that,
/// because a plan is keyed by the shape of the work and not by the rectangles a recording carries.
/// </para>
/// </remarks>
public sealed class OpaqueRenderDefinition<TState>
    where TState : notnull
{
    private readonly Action<OpaqueRenderSession, TState> _execute;
    private readonly OpaqueRenderBoundsContract _bounds;
    private readonly RenderHitTestContract _hitTest;
    private readonly RenderValueCardinality _valueCardinality;
    private readonly RenderScaleContract _scale;
    private readonly RenderDeviceGridSensitivity _deviceGridSensitivity;
    private readonly IReadOnlyList<RenderInputReadback> _inputReadbacks;
    private readonly IReadOnlyList<RenderResourceSlot> _resourceSlots;
    private readonly RenderInputDemandContract _inputDemand;

    private OpaqueRenderDefinition(
        Action<OpaqueRenderSession, TState> execute,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        IReadOnlyList<RenderInputReadback> inputReadbacks,
        IReadOnlyList<RenderResourceSlot> resourceSlots,
        RenderInputDemandContract inputDemand)
    {
        _execute = execute;
        _inputDemand = inputDemand;
        _bounds = bounds;
        _hitTest = hitTest;
        _valueCardinality = valueCardinality;
        _scale = scale;
        _deviceGridSensitivity = deviceGridSensitivity;
        _inputReadbacks = inputReadbacks;
        _resourceSlots = resourceSlots;
    }

    /// <summary>Creates an immutable opaque-operation definition.</summary>
    /// <remarks>
    /// <paramref name="inputDemand"/> declares what density each input has to reach for this operation's own
    /// resolved output demand. Only a combine or an expand may declare one, and it is what an operation that
    /// resamples its inputs asymmetrically needs: without it every input is asked for the unchanged output
    /// demand, so an unbounded input feeding an enlargement materializes below the density that enlargement
    /// consumes.
    /// </remarks>
    public static OpaqueRenderDefinition<TState> Create(
        Action<OpaqueRenderSession, TState> execute,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IEnumerable<RenderResourceSlot>? resources = null,
        RenderInputDemandContract inputDemand = default)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(bounds);
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        valueCardinality.ThrowIfUninitialized(nameof(valueCardinality));
        scale.ThrowIfUninitialized(nameof(scale));
        if (!Enum.IsDefined(deviceGridSensitivity))
            throw new ArgumentOutOfRangeException(nameof(deviceGridSensitivity));

        return new OpaqueRenderDefinition<TState>(
            execute,
            bounds,
            hitTest,
            valueCardinality,
            scale,
            deviceGridSensitivity,
            inputReadbacks?.ToArray() ?? [],
            RenderDescriptionValidation.CopyResourceSlots(resources, nameof(resources)),
            inputDemand);
    }

    /// <summary>Binds this operation shape to the state and resources for one recording.</summary>
    public OpaqueRenderCall<TState> Call(
        TState state,
        IEnumerable<RenderResourceBinding>? bindings = null)
        => new(this, state, bindings);

    internal OpaqueRenderDescription CreateDescription(
        TState state,
        IEnumerable<RenderResourceBinding>? bindings)
        => OpaqueRenderDescription.CreateCore(
            RenderDescriptionValidation.CreateStateChannel(
                state,
                _execute,
                nameof(state),
                nameof(_execute)),
            _bounds,
            _hitTest,
            _valueCardinality,
            _scale,
            _deviceGridSensitivity,
            definitionFingerprint: RenderDescriptionValidation.StructuralIdentityOfExecution(_execute),
            inputReadbacks: _inputReadbacks,
            resources: RenderDescriptionValidation.ValidateResourceBindings(
                _resourceSlots,
                bindings,
                nameof(bindings)),
            inputDemand: _inputDemand);
}

/// <summary>Binds one opaque-operation definition to one recording's state and resource tokens.</summary>
public sealed class OpaqueRenderCall<TState>
    where TState : notnull
{
    internal OpaqueRenderCall(
        OpaqueRenderDefinition<TState> definition,
        TState state,
        IEnumerable<RenderResourceBinding>? bindings)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        State = state;
        Description = definition.CreateDescription(state, bindings);
    }

    /// <summary>Gets the immutable operation shape.</summary>
    public OpaqueRenderDefinition<TState> Definition { get; }

    /// <summary>Gets the state supplied for this recording.</summary>
    public TState State { get; }

    internal OpaqueRenderDescription Description { get; }
}

/// <summary>
/// Defines the fixed shape of a painted source operation - a source that paints itself with a fill brush and a
/// stroke pen through an <see cref="ImmediateCanvas"/>.
/// </summary>
/// <typeparam name="TState">The per-recording state supplied by a <see cref="PaintedSourceCall{TState}"/>.</typeparam>
/// <remarks>
/// <para>
/// A definition contains only operation shape: its painting callback, its metadata contracts, and the resource
/// slots a call binds. Values that affect pixels belong to a call. When those values change, the owning
/// <see cref="RenderNode"/> must call <see cref="RenderNode.MarkChanged"/> before its next request.
/// </para>
/// <para>
/// The bounds contract is the one place a painted source departs from <see cref="OpaqueRenderDefinition{TState}"/>,
/// which holds its bounds as shape. What a painted source publishes is measured from the pen the same recording
/// supplies - <see cref="PenHelper.GetBounds(Rect, Pen.Resource)"/> over the painted rectangle - so a definition
/// holding it would have to be rebuilt whenever the pen moved, and no node could keep a <see langword="static"/>
/// <see langword="readonly"/> one. Carrying it on the call costs no plan: a bounds contract contributes only its
/// kind to the structural identity.
/// </para>
/// <para>
/// This is what the built-in shape and image nodes record through, and it does two things a hand-rolled
/// <see cref="OpaqueRenderDefinition{TState}"/> cannot do for itself: it keeps the callback's identity static so
/// the description can be replayed straight onto the destination canvas, and it withdraws that direct-replay
/// fast path when the fill or the pen resolves to a brush that itself draws, which has to be materialized first.
/// </para>
/// </remarks>
public sealed class PaintedSourceDefinition<TState>
    where TState : notnull
{
    private PaintedSourceDefinition(
        PaintedSourceDraw<TState> draw,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        bool paintsNonOverlappingCoverage,
        IReadOnlyList<RenderResourceSlot> resourceSlots)
    {
        Draw = draw;
        HitTest = hitTest;
        Scale = scale;
        DeviceGridSensitivity = deviceGridSensitivity;
        PaintsNonOverlappingCoverage = paintsNonOverlappingCoverage;
        ResourceSlots = resourceSlots;
    }

    /// <summary>Creates an immutable painted-source definition.</summary>
    /// <param name="draw">
    /// A non-null painting callback. Declare it as a static lambda so it carries no per-frame identity; see
    /// <see cref="PaintedSourceDraw{TState}"/>.
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
    /// <param name="paintsNonOverlappingCoverage">
    /// Whether the callback covers each pixel it paints at most once. That is what lets the source be painted
    /// straight into a destination-out composite instead of into an isolated layer first: coverage that doubles
    /// up would erase twice. Declare it <see langword="false"/> for a callback that strokes or fills over its own
    /// output - a waveform, a scatter, a path drawn in overlapping segments. The engine consults it only when the
    /// source is replayable at all: a fill or a pen that resolves to a brush which itself draws is materialized
    /// into its own layer regardless of what is declared here.
    /// </param>
    /// <param name="resources">
    /// The resource addresses this source reads, on top of the fill and the pen a call supplies. Each is bound to
    /// a request-scoped token by the call, and a hit test reaches one through
    /// <see cref="RenderHitTestContract.FromSlot{T}(RenderResourceSlot{T}, Func{T, Point, bool})"/>.
    /// </param>
    public static PaintedSourceDefinition<TState> Create(
        PaintedSourceDraw<TState> draw,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        bool paintsNonOverlappingCoverage = true,
        IEnumerable<RenderResourceSlot>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(draw);
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        if (!Enum.IsDefined(deviceGridSensitivity))
            throw new ArgumentOutOfRangeException(nameof(deviceGridSensitivity));

        return new PaintedSourceDefinition<TState>(
            draw,
            hitTest,
            scale,
            deviceGridSensitivity,
            paintsNonOverlappingCoverage,
            RenderDescriptionValidation.CopyResourceSlots(resources, nameof(resources)));
    }

    /// <summary>Binds this operation shape to the values and resources for one recording.</summary>
    /// <param name="state">
    /// The state the callback paints from. Treat it as immutable once recorded: the callback runs later, so a
    /// value mutated after this call changes what the fragment paints without the engine noticing.
    /// </param>
    /// <param name="fill">
    /// The fill brush the callback receives, or <see langword="null"/> for an unfilled source. A non-null brush is
    /// borrowed for the request, so the caller keeps ownership of it.
    /// </param>
    /// <param name="pen">
    /// The stroke pen the callback receives, or <see langword="null"/> for an unstroked source. A non-null pen is
    /// borrowed for the request, so the caller keeps ownership of it.
    /// </param>
    /// <param name="bounds">
    /// A source bounds contract over the finite, non-empty rectangle the callback paints within, in the node's own
    /// coordinate space. Build it with <see cref="OpaqueRenderBoundsContract.Source(Rect, Thickness)"/>, measuring
    /// the rectangle with <see cref="PenHelper.GetBounds(Rect, Pen.Resource)"/> so it follows the same
    /// stroke-alignment and offset convention as the built-in shape nodes, and give it a raster outset when
    /// filtering or anti-aliasing spills past what the source publishes.
    /// </param>
    /// <param name="bindings">
    /// One request-scoped token per resource slot the definition declares, each produced by
    /// <see cref="RenderResourceSlot{T}.Bind(RenderResource{T})"/>.
    /// </param>
    public PaintedSourceCall<TState> Call(
        TState state,
        Brush.Resource? fill,
        Pen.Resource? pen,
        OpaqueRenderBoundsContract bounds,
        IEnumerable<RenderResourceBinding>? bindings = null)
        => new(this, state, fill, pen, bounds, bindings);

    internal PaintedSourceDraw<TState> Draw { get; }

    internal RenderHitTestContract HitTest { get; }

    internal RenderScaleContract Scale { get; }

    internal RenderDeviceGridSensitivity DeviceGridSensitivity { get; }

    internal bool PaintsNonOverlappingCoverage { get; }

    internal IReadOnlyList<RenderResourceSlot> ResourceSlots { get; }
}

/// <summary>Binds one painted-source definition to one recording's values and resource tokens.</summary>
public sealed class PaintedSourceCall<TState>
    where TState : notnull
{
    internal PaintedSourceCall(
        PaintedSourceDefinition<TState> definition,
        TState state,
        Brush.Resource? fill,
        Pen.Resource? pen,
        OpaqueRenderBoundsContract bounds,
        IEnumerable<RenderResourceBinding>? bindings)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(bounds);
        bounds.ThrowIfIncompatible(OpaqueRenderTopology.Source, nameof(bounds));
        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(bounds.TransformBounds([]), nameof(bounds));

        Definition = definition;
        State = state;
        Fill = fill;
        Pen = pen;
        Bounds = bounds;
        Bindings = RenderDescriptionValidation.ValidateResourceBindings(
            definition.ResourceSlots,
            bindings,
            nameof(bindings));
    }

    /// <summary>Gets the immutable operation shape.</summary>
    public PaintedSourceDefinition<TState> Definition { get; }

    /// <summary>Gets the state supplied for this recording.</summary>
    public TState State { get; }

    /// <summary>Gets the fill brush supplied for this recording, or <see langword="null"/>.</summary>
    public Brush.Resource? Fill { get; }

    /// <summary>Gets the stroke pen supplied for this recording, or <see langword="null"/>.</summary>
    public Pen.Resource? Pen { get; }

    /// <summary>Gets the bounds contract supplied for this recording.</summary>
    public OpaqueRenderBoundsContract Bounds { get; }

    internal IReadOnlyList<RenderResourceBinding> Bindings { get; }
}

/// <summary>Defines the fixed shape of a guarded target-scope operation.</summary>
/// <typeparam name="TState">The per-recording state supplied by a <see cref="TargetScopeCall{TState}"/>.</typeparam>
/// <inheritdoc cref="OpaqueRenderDefinition{TState}" path="/remarks"/>
public sealed class TargetScopeDefinition<TState>
    where TState : notnull
{
    private readonly Action<TargetScopeSession, TState> _execute;
    private readonly RenderBoundsContract _bounds;
    private readonly RenderHitTestContract _hitTest;
    private readonly RenderScaleContract _scale;
    private readonly RenderDeviceGridSensitivity _deviceGridSensitivity;
    private readonly RenderDeviceGridMapping _deviceGridMapping;
    private readonly RenderScopeTransformSpace _transformSpace;
    private readonly IReadOnlyList<RenderResourceSlot> _resourceSlots;

    private TargetScopeDefinition(
        Action<TargetScopeSession, TState> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        RenderDeviceGridMapping deviceGridMapping,
        RenderScopeTransformSpace transformSpace,
        IReadOnlyList<RenderResourceSlot> resourceSlots)
    {
        _execute = execute;
        _bounds = bounds;
        _hitTest = hitTest;
        _scale = scale;
        _deviceGridSensitivity = deviceGridSensitivity;
        _deviceGridMapping = deviceGridMapping;
        _transformSpace = transformSpace;
        _resourceSlots = resourceSlots;
    }

    /// <summary>Creates an immutable guarded target-scope definition.</summary>
    /// <param name="transformSpace">
    /// The space the callback's replay transform is defined in. The default assumes the ambient target
    /// transform, which carries the scope's own scale; declare
    /// <see cref="RenderScopeTransformSpace.InputLogical"/> when the callback transforms its input in the
    /// input's own coordinates, so <paramref name="scale"/>'s backward map reaches it.
    /// </param>
    public static TargetScopeDefinition<TState> Create(
        Action<TargetScopeSession, TState> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        RenderDeviceGridMapping deviceGridMapping = RenderDeviceGridMapping.Remapped,
        RenderScopeTransformSpace transformSpace = RenderScopeTransformSpace.AmbientTarget,
        IEnumerable<RenderResourceSlot>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        bounds.ThrowIfUninitialized(nameof(bounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        if (!Enum.IsDefined(deviceGridSensitivity))
            throw new ArgumentOutOfRangeException(nameof(deviceGridSensitivity));
        if (!Enum.IsDefined(deviceGridMapping))
            throw new ArgumentOutOfRangeException(nameof(deviceGridMapping));
        if (!Enum.IsDefined(transformSpace))
            throw new ArgumentOutOfRangeException(nameof(transformSpace));

        return new TargetScopeDefinition<TState>(
            execute,
            bounds,
            hitTest,
            scale,
            deviceGridSensitivity,
            deviceGridMapping,
            transformSpace,
            RenderDescriptionValidation.CopyResourceSlots(resources, nameof(resources)));
    }

    /// <summary>Binds this operation shape to the state and resources for one recording.</summary>
    public TargetScopeCall<TState> Call(
        TState state,
        IEnumerable<RenderResourceBinding>? bindings = null)
        => new(this, state, bindings);

    internal TargetScopeDescription CreateDescription(
        TState state,
        IEnumerable<RenderResourceBinding>? bindings)
        => TargetScopeDescription.CreateCore(
            RenderDescriptionValidation.CreateStateChannel(
                state,
                _execute,
                nameof(state),
                nameof(_execute)),
            _bounds,
            _hitTest,
            _scale,
            _deviceGridSensitivity,
            _deviceGridMapping,
            definitionFingerprint: RenderDescriptionValidation.StructuralIdentityOfExecution(_execute),
            resources: RenderDescriptionValidation.ValidateResourceBindings(
                _resourceSlots,
                bindings,
                nameof(bindings)),
            isValueReplayMap: false,
            _transformSpace);
}

/// <summary>Binds one guarded target-scope definition to one recording's state and resource tokens.</summary>
public sealed class TargetScopeCall<TState>
    where TState : notnull
{
    internal TargetScopeCall(
        TargetScopeDefinition<TState> definition,
        TState state,
        IEnumerable<RenderResourceBinding>? bindings)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        State = state;
        Description = definition.CreateDescription(state, bindings);
    }

    /// <summary>Gets the immutable operation shape.</summary>
    public TargetScopeDefinition<TState> Definition { get; }

    /// <summary>Gets the state supplied for this recording.</summary>
    public TState State { get; }

    internal TargetScopeDescription Description { get; }
}

/// <summary>Defines the fixed shape of a guarded target-command operation.</summary>
/// <typeparam name="TState">The per-recording state supplied by a <see cref="TargetCommandCall{TState}"/>.</typeparam>
/// <remarks>
/// <para>
/// A definition contains only operation shape: its callback, metadata, and planner traits. Values that
/// affect pixels belong to a call. When those values change, the owning <see cref="RenderNode"/> must call
/// <see cref="RenderNode.MarkChanged"/> before its next request.
/// </para>
/// <para>
/// The affected region, query bounds, and hit-test contract are that metadata, and they answer before any
/// call supplies state. An operation whose target rectangle is itself a per-recording value builds its
/// definition inside <see cref="RenderNode.Process"/> rather than holding one - the shape
/// <c>ParticleRenderNode</c> uses, recomputing its region from live positions each recording. Nothing is
/// lost by that: the region is part of the fragment's identity by value, so supplying it later would
/// recompile exactly the same way.
/// </para>
/// </remarks>
public sealed class TargetCommandDefinition<TState>
    where TState : notnull
{
    private readonly Action<TargetCommandSession, TState> _execute;
    private readonly TargetRegion _affectedRegion;
    private readonly Rect _queryBounds;
    private readonly RenderHitTestContract _hitTest;
    private readonly TargetAccess _access;
    private readonly IReadOnlyList<RenderInputReadback> _inputReadbacks;
    private readonly RenderInputDemandContract _inputDemand;
    private readonly IReadOnlyList<RenderResourceSlot> _resourceSlots;

    private TargetCommandDefinition(
        Action<TargetCommandSession, TState> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access,
        IReadOnlyList<RenderInputReadback> inputReadbacks,
        RenderInputDemandContract inputDemand,
        IReadOnlyList<RenderResourceSlot> resourceSlots)
    {
        _execute = execute;
        _affectedRegion = affectedRegion;
        _queryBounds = queryBounds;
        _hitTest = hitTest;
        _access = access;
        _inputReadbacks = inputReadbacks;
        _inputDemand = inputDemand;
        _resourceSlots = resourceSlots;
    }

    /// <summary>Creates an immutable guarded target-command definition.</summary>
    public static TargetCommandDefinition<TState> Create(
        Action<TargetCommandSession, TState> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access = TargetAccess.ReadWrite,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IEnumerable<RenderResourceSlot>? resources = null,
        RenderInputDemandContract inputDemand = default)
    {
        ArgumentNullException.ThrowIfNull(execute);
        affectedRegion.ThrowIfUninitialized(nameof(affectedRegion));
        RenderRectValidation.ThrowIfInvalidInput(queryBounds, nameof(queryBounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        RenderDescriptionValidation.ThrowIfQueryContributionIncoherent(
            queryBounds,
            hitTest,
            nameof(hitTest));
        if (!Enum.IsDefined(access))
            throw new ArgumentOutOfRangeException(nameof(access));
        if (access == TargetAccess.Readback && affectedRegion.Kind == TargetRegionKind.Empty)
        {
            throw new ArgumentException(
                "A readback command requires a non-empty target region.",
                nameof(affectedRegion));
        }

        return new TargetCommandDefinition<TState>(
            execute,
            affectedRegion,
            queryBounds,
            hitTest,
            access,
            inputReadbacks?.ToArray() ?? [],
            inputDemand,
            RenderDescriptionValidation.CopyResourceSlots(resources, nameof(resources)));
    }

    /// <summary>Binds this operation shape to the state and resources for one recording.</summary>
    public TargetCommandCall<TState> Call(
        TState state,
        IEnumerable<RenderResourceBinding>? bindings = null)
        => new(this, state, bindings);

    internal TargetCommandDescription CreateDescription(
        TState state,
        IEnumerable<RenderResourceBinding>? bindings)
        => TargetCommandDescription.CreateCore(
            RenderDescriptionValidation.CreateStateChannel(
                state,
                _execute,
                nameof(state),
                nameof(_execute)),
            _affectedRegion,
            _queryBounds,
            _hitTest,
            _access,
            _inputReadbacks,
            definitionFingerprint: RenderDescriptionValidation.StructuralIdentityOfExecution(_execute),
            inputDemand: _inputDemand,
            resources: RenderDescriptionValidation.ValidateResourceBindings(
                _resourceSlots,
                bindings,
                nameof(bindings)));
}

/// <summary>Binds one guarded target-command definition to one recording's state and resource tokens.</summary>
public sealed class TargetCommandCall<TState>
    where TState : notnull
{
    internal TargetCommandCall(
        TargetCommandDefinition<TState> definition,
        TState state,
        IEnumerable<RenderResourceBinding>? bindings)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        State = state;
        Description = definition.CreateDescription(state, bindings);
    }

    /// <summary>Gets the immutable operation shape.</summary>
    public TargetCommandDefinition<TState> Definition { get; }

    /// <summary>Gets the state supplied for this recording.</summary>
    public TState State { get; }

    internal TargetCommandDescription Description { get; }
}

/// <summary>Defines the fixed shape of an opaque external target scope.</summary>
/// <remarks>
/// Raw target work is intentionally never eligible for persistent output reuse. Its definition still declares
/// metadata and resource slots so the invocation can be validated before execution.
/// <para>
/// Metadata is part of that shape: the bounds, hit-test, and scale contracts answer before any call
/// supplies state, so an operation whose extent or density is itself a per-recording value builds its
/// definition inside <see cref="RenderNode.Process"/> rather than holding one. Nothing is lost by that,
/// because a plan is keyed by the shape of the work and not by the values a recording carries.
/// </para>
/// </remarks>
public sealed class RawTargetScopeDefinition<TState>
    where TState : notnull
{
    private readonly Action<RawTargetScopeSession, TState> _execute;
    private readonly RenderBoundsContract _bounds;
    private readonly RenderHitTestContract _hitTest;
    private readonly RenderScaleContract _scale;
    private readonly IReadOnlyList<RenderResourceSlot> _resourceSlots;

    private RawTargetScopeDefinition(
        Action<RawTargetScopeSession, TState> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        IReadOnlyList<RenderResourceSlot> resourceSlots)
    {
        _execute = execute;
        _bounds = bounds;
        _hitTest = hitTest;
        _scale = scale;
        _resourceSlots = resourceSlots;
    }

    /// <summary>Creates an immutable raw target-scope definition.</summary>
    public static RawTargetScopeDefinition<TState> Create(
        Action<RawTargetScopeSession, TState> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        IEnumerable<RenderResourceSlot>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        bounds.ThrowIfUninitialized(nameof(bounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        return new RawTargetScopeDefinition<TState>(
            execute,
            bounds,
            hitTest,
            scale,
            RenderDescriptionValidation.CopyResourceSlots(resources, nameof(resources)));
    }

    /// <summary>Binds this raw scope to one recording's callback state and resource tokens.</summary>
    public RawTargetScopeCall<TState> Call(
        TState state,
        IEnumerable<RenderResourceBinding>? bindings = null)
        => new(this, state, bindings);

    internal RawTargetScopeDescription CreateDescription(
        TState state,
        IEnumerable<RenderResourceBinding>? bindings)
        => RawTargetScopeDescription.CreateCore(
            RenderDescriptionValidation.CreateStateChannel(
                state,
                _execute,
                nameof(state),
                nameof(_execute)),
            _bounds,
            _hitTest,
            _scale,
            RenderDescriptionValidation.StructuralIdentityOfExecution(_execute),
            RenderDescriptionValidation.ValidateResourceBindings(
                _resourceSlots,
                bindings,
                nameof(bindings)));
}

/// <summary>Binds one raw target-scope definition to one recording.</summary>
public sealed class RawTargetScopeCall<TState>
    where TState : notnull
{
    internal RawTargetScopeCall(
        RawTargetScopeDefinition<TState> definition,
        TState state,
        IEnumerable<RenderResourceBinding>? bindings)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        State = state;
        Description = definition.CreateDescription(state, bindings);
    }

    /// <summary>Gets the immutable operation shape.</summary>
    public RawTargetScopeDefinition<TState> Definition { get; }

    /// <summary>Gets the callback state supplied for this recording.</summary>
    public TState State { get; }

    internal RawTargetScopeDescription Description { get; }
}

/// <summary>Defines the fixed shape of an opaque external target command.</summary>
/// <remarks>
/// Raw target work is intentionally never eligible for persistent output reuse. Its definition still declares
/// metadata and resource slots so the invocation can be validated before execution.
/// <para>
/// Metadata is part of that shape: the query bounds and hit-test contract answer before any call supplies
/// state, so an operation whose queried extent is itself a per-recording value builds its definition inside
/// <see cref="RenderNode.Process"/> rather than holding one. Nothing is lost by that, because a plan is
/// keyed by the shape of the work and not by the values a recording carries.
/// </para>
/// </remarks>
public sealed class RawTargetCommandDefinition<TState>
    where TState : notnull
{
    private readonly Action<RawTargetCommandSession, TState> _execute;
    private readonly Rect _queryBounds;
    private readonly RenderHitTestContract _hitTest;
    private readonly IReadOnlyList<RenderResourceSlot> _resourceSlots;

    private RawTargetCommandDefinition(
        Action<RawTargetCommandSession, TState> execute,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        IReadOnlyList<RenderResourceSlot> resourceSlots)
    {
        _execute = execute;
        _queryBounds = queryBounds;
        _hitTest = hitTest;
        _resourceSlots = resourceSlots;
    }

    /// <summary>Creates an immutable raw target-command definition.</summary>
    public static RawTargetCommandDefinition<TState> Create(
        Action<RawTargetCommandSession, TState> execute,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        IEnumerable<RenderResourceSlot>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        RenderRectValidation.ThrowIfInvalidInput(queryBounds, nameof(queryBounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        if (hitTest.Kind == RenderHitTestContractKind.AnyInput)
        {
            throw new ArgumentException(
                "A raw target command has no logical value inputs and cannot use AnyInput hit testing.",
                nameof(hitTest));
        }
        RenderDescriptionValidation.ThrowIfQueryContributionIncoherent(
            queryBounds,
            hitTest,
            nameof(hitTest));
        return new RawTargetCommandDefinition<TState>(
            execute,
            queryBounds,
            hitTest,
            RenderDescriptionValidation.CopyResourceSlots(resources, nameof(resources)));
    }

    /// <summary>Binds this raw command to one recording's callback state and resource tokens.</summary>
    public RawTargetCommandCall<TState> Call(
        TState state,
        IEnumerable<RenderResourceBinding>? bindings = null)
        => new(this, state, bindings);

    internal RawTargetCommandDescription CreateDescription(
        TState state,
        IEnumerable<RenderResourceBinding>? bindings)
        => RawTargetCommandDescription.CreateCore(
            RenderDescriptionValidation.CreateStateChannel(
                state,
                _execute,
                nameof(state),
                nameof(_execute)),
            _queryBounds,
            _hitTest,
            RenderDescriptionValidation.StructuralIdentityOfExecution(_execute),
            RenderDescriptionValidation.ValidateResourceBindings(
                _resourceSlots,
                bindings,
                nameof(bindings)));
}

/// <summary>Binds one raw target-command definition to one recording.</summary>
public sealed class RawTargetCommandCall<TState>
    where TState : notnull
{
    internal RawTargetCommandCall(
        RawTargetCommandDefinition<TState> definition,
        TState state,
        IEnumerable<RenderResourceBinding>? bindings)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        State = state;
        Description = definition.CreateDescription(state, bindings);
    }

    /// <summary>Gets the immutable operation shape.</summary>
    public RawTargetCommandDefinition<TState> Definition { get; }

    /// <summary>Gets the callback state supplied for this recording.</summary>
    public TState State { get; }

    internal RawTargetCommandDescription Description { get; }
}
