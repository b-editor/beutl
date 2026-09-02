namespace Beutl.Graphics.Rendering;

public sealed class TargetScopeDescription
{
    private readonly RenderExecutionChannel<TargetScopeSession> _execution;

    private TargetScopeDescription(
        RenderExecutionChannel<TargetScopeSession> execution,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        RenderDeviceGridMapping deviceGridMapping,
        object definitionFingerprint,
        IReadOnlyList<RenderResourceBinding> resources,
        bool isValueReplayMap,
        RenderScopeTransformSpace transformSpace,
        bool builtInBackdropCapturesBackingTarget)
    {
        _execution = execution;
        Bounds = bounds;
        HitTest = hitTest;
        Scale = scale;
        DeviceGridSensitivity = deviceGridSensitivity;
        DeviceGridMapping = deviceGridMapping;
        DefinitionFingerprint = definitionFingerprint;
        Resources = resources;
        IsValueReplayMap = isValueReplayMap;
        TransformSpace = transformSpace;
        BuiltInBackdropCapturesBackingTarget = builtInBackdropCapturesBackingTarget;
    }

    public RenderBoundsContract Bounds { get; }

    public RenderHitTestContract HitTest { get; }

    public RenderScaleContract Scale { get; }

    /// <summary>Gets whether this scope's own replay or clip coverage depends on device-grid phase.</summary>
    public RenderDeviceGridSensitivity DeviceGridSensitivity { get; }

    /// <summary>Gets the declared device pixel grid this scope replays its input onto.</summary>
    public RenderDeviceGridMapping DeviceGridMapping { get; }

    internal object DefinitionFingerprint { get; }

    public IReadOnlyList<RenderResourceBinding> Resources { get; }

    internal void Execute(TargetScopeSession session) => _execution.Invoke(session);

    /// <summary>Gets whether the renderer lowers this scope into the value graph.</summary>
    /// <remarks>
    /// Engine-owned and not declarable: it requires the callback to be mechanically restricted to
    /// allocation-free target state plus exactly one replay. <see cref="TransformSpace"/> is the separate,
    /// author-declarable question of where the replay transform lives.
    /// </remarks>
    internal bool IsValueReplayMap { get; }

    /// <summary>Gets the space this scope's replay transform is defined in.</summary>
    public RenderScopeTransformSpace TransformSpace { get; }

    internal bool BuiltInBackdropCapturesBackingTarget { get; }

    /// <param name="state">
    /// Immutable pixel-affecting state retained for execution.
    /// </param>
    /// <param name="deviceGridSensitivity">
    /// Whether replay or clip coverage changes with device-grid phase.
    /// </param>
    /// <param name="execute">
    /// A static execution callback.
    /// </param>
    /// <param name="deviceGridMapping">
    /// Whether replay preserves the input device grid. The default assumes remapping.
    /// </param>
    /// <param name="slots">
    /// Declared slots. <paramref name="resources"/> must bind each exactly once.
    /// </param>
    public static TargetScopeDescription Create<TState>(
        TState state,
        Action<TargetScopeSession, TState> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        RenderDeviceGridMapping deviceGridMapping = RenderDeviceGridMapping.Remapped,
        RenderScopeTransformSpace transformSpace = RenderScopeTransformSpace.AmbientTarget,
        IEnumerable<RenderResourceBinding>? resources = null,
        IEnumerable<RenderResourceSlot>? slots = null)
        where TState : notnull
        => CreateCore(
            RenderDescriptionValidation.CreateStateChannel(
                state,
                execute,
                nameof(state),
                nameof(execute)),
            bounds,
            hitTest,
            scale,
            deviceGridSensitivity,
            deviceGridMapping,
            RenderDescriptionValidation.StructuralIdentityOfExecution(execute),
            RenderDescriptionValidation.BindDeclaredSlots(
                slots,
                resources,
                nameof(slots),
                nameof(resources)),
            isValueReplayMap: false,
            transformSpace,
            builtInBackdropCapturesBackingTarget: false);

    /// <summary>
    /// Creates a scope whose output can never satisfy a later request's cache lookup.
    /// </summary>
    /// <remarks>
    /// The opt-out for a callback whose pixel-affecting state cannot be expressed as copied, deeply immutable
    /// CPU state. The callback may capture, and the recorded output takes a fresh request-local identity every
    /// time.
    /// </remarks>
    internal static TargetScopeDescription CreateRequestLocal(
        Action<TargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        RenderDeviceGridMapping deviceGridMapping = RenderDeviceGridMapping.Remapped,
        RenderScopeTransformSpace transformSpace = RenderScopeTransformSpace.AmbientTarget,
        IEnumerable<RenderResourceBinding>? resources = null)
        => CreateCore(
            RenderDescriptionValidation.CreateRequestLocalChannel(execute, nameof(execute)),
            bounds,
            hitTest,
            scale,
            deviceGridSensitivity,
            deviceGridMapping,
            execute,
            resources,
            isValueReplayMap: false,
            transformSpace,
            builtInBackdropCapturesBackingTarget: false);

    /// <summary>
    /// Creates a scope the renderer lowers into the value graph instead of materializing its input.
    /// </summary>
    /// <remarks>
    /// Eligibility is engine-owned because no declaration can establish it: the callback must be mechanically
    /// restricted to allocation-free target state plus exactly one replay, which only an in-engine author can
    /// guarantee. Public <see cref="Create"/> therefore always produces a materializing boundary.
    /// </remarks>
    internal static TargetScopeDescription CreateValueReplayMap<TState>(
        TState state,
        Action<TargetScopeSession, TState> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        RenderDeviceGridMapping deviceGridMapping,
        bool builtInBackdropCapturesBackingTarget = false,
        IEnumerable<RenderResourceBinding>? resources = null)
        where TState : notnull
        => CreateCore(
            RenderDescriptionValidation.CreateStateChannel(
                state,
                execute,
                nameof(state),
                nameof(execute)),
            bounds,
            hitTest,
            scale,
            deviceGridSensitivity,
            deviceGridMapping,
            new EngineValueReplayMapDefinition(
                RenderDescriptionValidation.StructuralIdentityOfExecution(execute)),
            resources,
            isValueReplayMap: true,
            // A value replay map is lowered into the value graph, which only holds together when the
            // transform between the scope and its input is expressed in the input's own coordinates.
            RenderScopeTransformSpace.InputLogical,
            builtInBackdropCapturesBackingTarget);

    internal static TargetScopeDescription CreateCore(
        RenderExecutionChannel<TargetScopeSession> execution,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        RenderDeviceGridMapping deviceGridMapping,
        object definitionFingerprint,
        IEnumerable<RenderResourceBinding>? resources,
        bool isValueReplayMap,
        RenderScopeTransformSpace transformSpace,
        bool builtInBackdropCapturesBackingTarget = false)
    {
        bounds.ThrowIfUninitialized(nameof(bounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        if (!Enum.IsDefined(deviceGridSensitivity))
            throw new ArgumentOutOfRangeException(nameof(deviceGridSensitivity));
        if (!Enum.IsDefined(deviceGridMapping))
            throw new ArgumentOutOfRangeException(nameof(deviceGridMapping));
        if (!Enum.IsDefined(transformSpace))
            throw new ArgumentOutOfRangeException(nameof(transformSpace));
        ArgumentNullException.ThrowIfNull(definitionFingerprint);

        return new TargetScopeDescription(
            execution,
            bounds,
            hitTest,
            scale,
            deviceGridSensitivity,
            deviceGridMapping,
            definitionFingerprint,
            RenderDescriptionValidation.CopyResourceBindings(resources, nameof(resources)),
            isValueReplayMap,
            transformSpace,
            builtInBackdropCapturesBackingTarget);
    }
}

internal sealed record EngineValueReplayMapDefinition(object Execute);
