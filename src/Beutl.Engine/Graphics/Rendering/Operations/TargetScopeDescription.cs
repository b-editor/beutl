namespace Beutl.Graphics.Rendering;

/// <summary>
/// Declares whether a guarded target scope replays its input onto the device pixel grid the input would have
/// been rasterized against without the scope.
/// </summary>
/// <remarks>
/// A scope callback's whole permitted vocabulary is save/restore, transform, and clip, so moving the replayed
/// content onto a different grid is an ordinary thing for a scope to do rather than an exception. The planner
/// therefore assumes <see cref="Remapped"/> unless the scope states otherwise: upstream content that declares
/// <see cref="RenderDeviceGridSensitivity.PhaseDependent"/> is re-rasterized under a remapping scope instead
/// of being resampled out of an output cache.
/// </remarks>
public enum RenderDeviceGridMapping : byte
{
    /// <summary>
    /// The scope may replay its input onto a different device pixel grid. Declaring this for a scope that in
    /// fact preserves the grid only costs upstream cache reuse; it never produces wrong pixels.
    /// </summary>
    Remapped,

    /// <summary>
    /// The scope replays its input onto the same device pixel grid, so device-grid phase dependent content
    /// upstream keeps the phase its cached output was captured at.
    /// </summary>
    Preserved,
}

/// <summary>
/// Declares the space a guarded target scope's replay transform is defined in.
/// </summary>
/// <remarks>
/// A scope's declared <see cref="RenderScaleContract"/> can carry an output demand back to its input only when
/// the transform between them is expressed in the input's own coordinates. A scope defined against the ambient
/// target transform - what <c>TransformOperator.Append</c> and <c>TransformOperator.Set</c> do - has that scale
/// carried by the destination matrix instead, which the value graph has no representation of, so raising the
/// input's demand there would rasterize it enlarged and then draw it enlarged again.
/// </remarks>
public enum RenderScopeTransformSpace : byte
{
    /// <summary>
    /// The replay transform is defined against the ambient target transform. The scale contract's backward
    /// map is not applied, because the destination already carries whatever the scope contributes.
    /// </summary>
    AmbientTarget,

    /// <summary>
    /// The replay transform is defined in the input's own logical space, so the scale contract describes the
    /// step between them completely and its backward map reaches the input.
    /// </summary>
    InputLogical,
}

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
    /// Every pixel-affecting value the callback reads. It belongs in the call state; when it changes, the owning
    /// node reports the change through <see cref="RenderNode.HasChanges"/>.
    /// </param>
    /// <param name="deviceGridSensitivity">
    /// Whether this scope's replay or surrounding clip state changes coverage with device-grid phase. The
    /// conservative default requires an explicit <see cref="RenderDeviceGridSensitivity.Insensitive"/> promise.
    /// </param>
    /// <param name="execute">
    /// A non-capturing callback. Declare it <see langword="static"/>: a capture would let a per-frame value
    /// shape the output without reaching <paramref name="state"/>, and is rejected.
    /// </param>
    /// <param name="deviceGridMapping">
    /// The device pixel grid the callback replays its input onto. The default assumes a different grid;
    /// declare <see cref="RenderDeviceGridMapping.Preserved"/> only when the callback leaves the target
    /// transform alone.
    /// </param>
    /// <param name="slots">
    /// The resource slots this operation declares. <paramref name="resources"/> must bind every one of them
    /// exactly once and is reordered into this list's order, so the order the caller wrote the bindings in
    /// never reaches the recorded operation. Omitting the list declares no slots rather than skipping that
    /// check, so binding a resource without declaring its slot is an error.
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

public sealed class TargetScopeSession
{
    private readonly RenderExecutionSessionToken _token;
    private readonly Rect _outputBounds;
    private readonly Rect _requiredRegion;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly RenderCallbackCanvas _canvas;
    private readonly IReadOnlyList<RenderResourceBinding> _resourceBindings;
    private readonly IReadOnlyList<RenderResource> _resources;
    private readonly Action<ImmediateCanvas> _replayInput;
    private bool _replayed;

    internal TargetScopeSession(
        RenderExecutionSessionToken token,
        Rect outputBounds,
        Rect requiredRegion,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        RenderCallbackCanvas canvas,
        IReadOnlyList<RenderResourceBinding> resources,
        Action<ImmediateCanvas> replayInput)
    {
        _token = token;
        _outputBounds = outputBounds;
        _requiredRegion = requiredRegion;
        _intent = intent;
        _purpose = purpose;
        _canvas = canvas;
        _resourceBindings = resources;
        _resources = resources.SelectToArray(static binding => binding.Resource);
        _replayInput = replayInput;
    }

    public Rect OutputBounds
    {
        get { _token.ThrowIfInactive(); return _outputBounds; }
    }

    public Rect RequiredRegion
    {
        get { _token.ThrowIfInactive(); return _requiredRegion; }
    }

    public RenderIntent Intent
    {
        get { _token.ThrowIfInactive(); return _intent; }
    }

    public RenderRequestPurpose Purpose
    {
        get { _token.ThrowIfInactive(); return _purpose; }
    }

    public RenderCallbackCanvas Canvas
    {
        get { _token.ThrowIfInactive(); return _canvas; }
    }

    public void ReplayInput()
    {
        _token.ThrowIfInactive();
        if (_replayed)
            throw new InvalidOperationException("A target scope input must be replayed exactly once.");

        ImmediateCanvas canvas = _token.GetActiveCanvas(_canvas);
        _replayed = true;
        canvas.ReplayTargetScopeInput(_replayInput);
    }

    /// <summary>Uses the resource bound to a declared slot.</summary>
    public void UseResource<T>(RenderResourceSlot<T> slot, Action<T> use)
        where T : class
    {
        _token.UseResource(slot, _resourceBindings, use);
    }

    internal void ValidateCompletion()
    {
        _token.ThrowIfInactive();
        if (!_replayed)
            throw new InvalidOperationException("A target scope input must be replayed exactly once.");
    }
}

internal sealed record EngineValueReplayMapDefinition(object Execute);

public sealed class RawTargetScopeDescription
{
    private readonly RenderExecutionChannel<RawTargetScopeSession> _execution;

    private RawTargetScopeDescription(
        RenderExecutionChannel<RawTargetScopeSession> execution,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        object definitionFingerprint,
        IReadOnlyList<RenderResourceBinding> resources)
    {
        _execution = execution;
        Bounds = bounds;
        HitTest = hitTest;
        Scale = scale;
        DefinitionFingerprint = definitionFingerprint;
        Resources = resources;
    }

    public RenderBoundsContract Bounds { get; }

    public RenderHitTestContract HitTest { get; }

    public RenderScaleContract Scale { get; }

    internal object DefinitionFingerprint { get; }

    public IReadOnlyList<RenderResourceBinding> Resources { get; }

    internal void Execute(RawTargetScopeSession session) => _execution.Invoke(session);

    /// <summary>Creates an immutable raw target-scope description.</summary>
    /// <param name="state">
    /// Every pixel-affecting value the callback reads. It belongs in the call state; when it changes, the owning
    /// node reports the change through <see cref="RenderNode.HasChanges"/>.
    /// </param>
    /// <param name="execute">
    /// A non-capturing callback. Declare it <see langword="static"/>: a capture would let a per-frame value
    /// shape what is drawn without reaching <paramref name="state"/>, and is rejected.
    /// </param>
    /// <param name="slots">
    /// The resource slots this operation declares. <paramref name="resources"/> must bind every one of them
    /// exactly once and is reordered into this list's order, so the order the caller wrote the bindings in
    /// never reaches the recorded operation. Omitting the list declares no slots rather than skipping that
    /// check, so binding a resource without declaring its slot is an error.
    /// </param>
    /// <remarks>
    /// The raw canvas keeps the recorded work opaque to the renderer, so a raw scope is never eligible for
    /// persistent output reuse whichever form built it. What the state-passing form buys is the identity the
    /// planner keys the shape of the work by: a static callback recorded twice is one plan, where
    /// <see cref="CreateRequestLocal"/> mints a fresh identity every recording.
    /// </remarks>
    public static RawTargetScopeDescription Create<TState>(
        TState state,
        Action<RawTargetScopeSession, TState> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
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
            RenderDescriptionValidation.StructuralIdentityOfExecution(execute),
            RenderDescriptionValidation.BindDeclaredSlots(
                slots,
                resources,
                nameof(slots),
                nameof(resources)));

    /// <summary>
    /// Creates a raw scope whose recorded work can never satisfy a later request's plan lookup.
    /// </summary>
    /// <remarks>
    /// The opt-out for a callback whose pixel-affecting state cannot be expressed as copied, deeply immutable
    /// CPU state. The callback may capture, and the recorded work takes a fresh request-local identity every
    /// time.
    /// </remarks>
    internal static RawTargetScopeDescription CreateRequestLocal(
        Action<RawTargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        IEnumerable<RenderResourceBinding>? resources = null)
        => CreateCore(
            RenderDescriptionValidation.CreateRequestLocalChannel(execute, nameof(execute)),
            bounds,
            hitTest,
            scale,
            execute,
            resources);

    internal static RawTargetScopeDescription CreateCore(
        RenderExecutionChannel<RawTargetScopeSession> execution,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        object definitionFingerprint,
        IEnumerable<RenderResourceBinding>? resources)
    {
        bounds.ThrowIfUninitialized(nameof(bounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        ArgumentNullException.ThrowIfNull(definitionFingerprint);

        return new RawTargetScopeDescription(
            execution,
            bounds,
            hitTest,
            scale,
            definitionFingerprint,
            RenderDescriptionValidation.CopyResourceBindings(resources, nameof(resources)));
    }
}

public sealed class RawTargetScopeSession
{
    private readonly RenderExecutionSessionToken _token;
    private readonly ImmediateCanvas _canvas;
    private readonly Rect _outputBounds;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly IReadOnlyList<RenderResourceBinding> _resourceBindings;
    private readonly IReadOnlyList<RenderResource> _resources;
    private readonly Action<ImmediateCanvas> _replayInput;
    private bool _replayed;

    internal RawTargetScopeSession(
        RenderExecutionSessionToken token,
        ImmediateCanvas canvas,
        Rect outputBounds,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        IReadOnlyList<RenderResourceBinding> resources,
        Action<ImmediateCanvas> replayInput)
    {
        _token = token;
        _canvas = canvas;
        _outputBounds = outputBounds;
        _intent = intent;
        _purpose = purpose;
        _resourceBindings = resources;
        _resources = resources.SelectToArray(static binding => binding.Resource);
        _replayInput = replayInput;
    }

    public ImmediateCanvas Canvas
    {
        get { _token.ThrowIfInactive(); return _canvas; }
    }

    public Rect OutputBounds
    {
        get { _token.ThrowIfInactive(); return _outputBounds; }
    }

    public RenderIntent Intent
    {
        get { _token.ThrowIfInactive(); return _intent; }
    }

    public RenderRequestPurpose Purpose
    {
        get { _token.ThrowIfInactive(); return _purpose; }
    }

    public void ReplayInput()
    {
        _token.ThrowIfInactive();
        if (_replayed)
            throw new InvalidOperationException("A raw target scope input must be replayed exactly once.");
        if (!_token.IsActiveCanvas(_canvas))
            throw new InvalidOperationException("ReplayInput must be called while the raw callback canvas is active.");

        _replayed = true;
        _replayInput(_canvas);
    }

    /// <summary>Uses the resource bound to a declared slot.</summary>
    /// <remarks>
    /// The addressing mode a reusable operation shape needs: its callback is static and its slots are fixed, so
    /// the token changes per call and only the slot names it from inside the callback.
    /// </remarks>
    public void UseResource<T>(RenderResourceSlot<T> slot, Action<T> use)
        where T : class
    {
        _token.UseResource(slot, _resourceBindings, use);
    }

    /// <summary>Uses a resource by its token.</summary>
    /// <remarks>For a request-local callback, which may capture the tokens it needs.</remarks>
    public void UseResource<T>(RenderResource<T> resource, Action<T> use)
        where T : class
    {
        _token.UseResource(resource, _resources, use);
    }

    internal void ValidateCompletion()
    {
        _token.ThrowIfInactive();
        if (!_replayed)
            throw new InvalidOperationException("A raw target scope input must be replayed exactly once.");
    }
}

public sealed class RawTargetCommandDescription
{
    private readonly RenderExecutionChannel<RawTargetCommandSession> _execution;

    private RawTargetCommandDescription(
        RenderExecutionChannel<RawTargetCommandSession> execution,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        object definitionFingerprint,
        IReadOnlyList<RenderResourceBinding> resources)
    {
        _execution = execution;
        QueryBounds = queryBounds;
        HitTest = hitTest;
        DefinitionFingerprint = definitionFingerprint;
        Resources = resources;
    }

    public Rect QueryBounds { get; }

    public RenderHitTestContract HitTest { get; }

    internal object DefinitionFingerprint { get; }

    public IReadOnlyList<RenderResourceBinding> Resources { get; }

    internal void Execute(RawTargetCommandSession session) => _execution.Invoke(session);

    /// <summary>Creates an immutable raw target-command description.</summary>
    /// <param name="state">
    /// Every pixel-affecting value the callback reads. It belongs in the call state; when it changes, the owning
    /// node reports the change through <see cref="RenderNode.HasChanges"/>.
    /// </param>
    /// <param name="execute">
    /// A non-capturing callback. Declare it <see langword="static"/>: a capture would let a per-frame value
    /// shape what is drawn without reaching <paramref name="state"/>, and is rejected.
    /// </param>
    /// <param name="slots">
    /// The resource slots this operation declares. <paramref name="resources"/> must bind every one of them
    /// exactly once and is reordered into this list's order, so the order the caller wrote the bindings in
    /// never reaches the recorded operation. Omitting the list declares no slots rather than skipping that
    /// check, so binding a resource without declaring its slot is an error.
    /// </param>
    /// <inheritdoc cref="RawTargetScopeDescription.Create{TState}" path="/remarks"/>
    public static RawTargetCommandDescription Create<TState>(
        TState state,
        Action<RawTargetCommandSession, TState> execute,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        IEnumerable<RenderResourceBinding>? resources = null,
        IEnumerable<RenderResourceSlot>? slots = null)
        where TState : notnull
        => CreateCore(
            RenderDescriptionValidation.CreateStateChannel(
                state,
                execute,
                nameof(state),
                nameof(execute)),
            queryBounds,
            hitTest,
            RenderDescriptionValidation.StructuralIdentityOfExecution(execute),
            RenderDescriptionValidation.BindDeclaredSlots(
                slots,
                resources,
                nameof(slots),
                nameof(resources)));

    /// <summary>
    /// Creates a raw command whose recorded work can never satisfy a later request's plan lookup.
    /// </summary>
    /// <inheritdoc cref="RawTargetScopeDescription.CreateRequestLocal" path="/remarks"/>
    internal static RawTargetCommandDescription CreateRequestLocal(
        Action<RawTargetCommandSession> execute,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        IEnumerable<RenderResourceBinding>? resources = null)
        => CreateCore(
            RenderDescriptionValidation.CreateRequestLocalChannel(execute, nameof(execute)),
            queryBounds,
            hitTest,
            execute,
            resources);

    internal static RawTargetCommandDescription CreateCore(
        RenderExecutionChannel<RawTargetCommandSession> execution,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        object definitionFingerprint,
        IEnumerable<RenderResourceBinding>? resources)
    {
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
        ArgumentNullException.ThrowIfNull(definitionFingerprint);

        return new RawTargetCommandDescription(
            execution,
            queryBounds,
            hitTest,
            definitionFingerprint,
            RenderDescriptionValidation.CopyResourceBindings(resources, nameof(resources)));
    }
}

public sealed class RawTargetCommandSession
{
    private readonly RenderExecutionSessionToken _token;
    private readonly ImmediateCanvas _canvas;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly IReadOnlyList<RenderResourceBinding> _resourceBindings;
    private readonly IReadOnlyList<RenderResource> _resources;

    internal RawTargetCommandSession(
        RenderExecutionSessionToken token,
        ImmediateCanvas canvas,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        IReadOnlyList<RenderResourceBinding> resources)
    {
        _token = token;
        _canvas = canvas;
        _intent = intent;
        _purpose = purpose;
        _resourceBindings = resources;
        _resources = resources.SelectToArray(static binding => binding.Resource);
    }

    public ImmediateCanvas Canvas
    {
        get { _token.ThrowIfInactive(); return _canvas; }
    }

    public RenderIntent Intent
    {
        get { _token.ThrowIfInactive(); return _intent; }
    }

    public RenderRequestPurpose Purpose
    {
        get { _token.ThrowIfInactive(); return _purpose; }
    }

    /// <summary>Uses the resource bound to a declared slot.</summary>
    /// <remarks>
    /// The addressing mode a reusable operation shape needs: its callback is static and its slots are fixed, so
    /// the token changes per call and only the slot names it from inside the callback.
    /// </remarks>
    public void UseResource<T>(RenderResourceSlot<T> slot, Action<T> use)
        where T : class
    {
        _token.UseResource(slot, _resourceBindings, use);
    }

    /// <summary>Uses a resource by its token.</summary>
    /// <remarks>For a request-local callback, which may capture the tokens it needs.</remarks>
    public void UseResource<T>(RenderResource<T> resource, Action<T> use)
        where T : class
    {
        _token.UseResource(resource, _resources, use);
    }
}
