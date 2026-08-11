namespace Beutl.Graphics.Rendering;

/// <summary>
/// Defines the fixed shape of an opaque render operation.
/// </summary>
/// <typeparam name="TState">The per-recording state supplied by an <see cref="OpaqueRenderCall{TState}"/>.</typeparam>
/// <remarks>
/// A definition contains only operation shape: its callback, metadata contracts, and planner traits. Values that
/// affect pixels belong to a call. When those values change, the owning <see cref="RenderNode"/> must set
/// <see cref="RenderNode.HasChanges"/> before its next request.
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

    private OpaqueRenderDefinition(
        Action<OpaqueRenderSession, TState> execute,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        IReadOnlyList<RenderInputReadback> inputReadbacks,
        IReadOnlyList<RenderResourceSlot> resourceSlots)
    {
        _execute = execute;
        _bounds = bounds;
        _hitTest = hitTest;
        _valueCardinality = valueCardinality;
        _scale = scale;
        _deviceGridSensitivity = deviceGridSensitivity;
        _inputReadbacks = inputReadbacks;
        _resourceSlots = resourceSlots;
    }

    /// <summary>Creates an immutable opaque-operation definition.</summary>
    public static OpaqueRenderDefinition<TState> Create(
        Action<OpaqueRenderSession, TState> execute,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IEnumerable<RenderResourceSlot>? resources = null)
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
            RenderDescriptionValidation.CopyResourceSlots(resources, nameof(resources)));
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
            definitionFingerprint: _execute.Method,
            inputReadbacks: _inputReadbacks,
            resources: RenderDescriptionValidation.ValidateResourceBindings(
                _resourceSlots,
                bindings,
                nameof(bindings)));
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

/// <summary>Defines the fixed shape of a guarded target-scope operation.</summary>
/// <typeparam name="TState">The per-recording state supplied by a <see cref="TargetScopeCall{TState}"/>.</typeparam>
public sealed class TargetScopeDefinition<TState>
    where TState : notnull
{
    private readonly Action<TargetScopeSession, TState> _execute;
    private readonly RenderBoundsContract _bounds;
    private readonly RenderHitTestContract _hitTest;
    private readonly RenderScaleContract _scale;
    private readonly RenderDeviceGridSensitivity _deviceGridSensitivity;
    private readonly RenderDeviceGridMapping _deviceGridMapping;
    private readonly IReadOnlyList<RenderResourceSlot> _resourceSlots;

    private TargetScopeDefinition(
        Action<TargetScopeSession, TState> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        RenderDeviceGridMapping deviceGridMapping,
        IReadOnlyList<RenderResourceSlot> resourceSlots)
    {
        _execute = execute;
        _bounds = bounds;
        _hitTest = hitTest;
        _scale = scale;
        _deviceGridSensitivity = deviceGridSensitivity;
        _deviceGridMapping = deviceGridMapping;
        _resourceSlots = resourceSlots;
    }

    /// <summary>Creates an immutable guarded target-scope definition.</summary>
    public static TargetScopeDefinition<TState> Create(
        Action<TargetScopeSession, TState> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        RenderDeviceGridMapping deviceGridMapping = RenderDeviceGridMapping.Remapped,
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

        return new TargetScopeDefinition<TState>(
            execute,
            bounds,
            hitTest,
            scale,
            deviceGridSensitivity,
            deviceGridMapping,
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
            definitionFingerprint: _execute.Method,
            resources: RenderDescriptionValidation.ValidateResourceBindings(
                _resourceSlots,
                bindings,
                nameof(bindings)),
            isValueReplayMap: false);
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
public sealed class TargetCommandDefinition<TState>
    where TState : notnull
{
    private readonly Action<TargetCommandSession, TState> _execute;
    private readonly TargetRegion _affectedRegion;
    private readonly Rect _queryBounds;
    private readonly RenderHitTestContract _hitTest;
    private readonly TargetAccess _access;
    private readonly IReadOnlyList<RenderInputReadback> _inputReadbacks;
    private readonly IReadOnlyList<RenderResourceSlot> _resourceSlots;

    private TargetCommandDefinition(
        Action<TargetCommandSession, TState> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access,
        IReadOnlyList<RenderInputReadback> inputReadbacks,
        IReadOnlyList<RenderResourceSlot> resourceSlots)
    {
        _execute = execute;
        _affectedRegion = affectedRegion;
        _queryBounds = queryBounds;
        _hitTest = hitTest;
        _access = access;
        _inputReadbacks = inputReadbacks;
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
        IEnumerable<RenderResourceSlot>? resources = null)
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
            definitionFingerprint: _execute.Method,
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
            _execute.Method,
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
            _execute.Method,
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
