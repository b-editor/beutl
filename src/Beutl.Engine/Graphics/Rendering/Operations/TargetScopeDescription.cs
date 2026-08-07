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

public sealed class TargetScopeDescription
{
    private readonly RenderExecutionChannel<TargetScopeSession> _execution;

    private TargetScopeDescription(
        RenderExecutionChannel<TargetScopeSession> execution,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridMapping deviceGridMapping,
        object structuralKey,
        IReadOnlyList<RenderResource> resources,
        bool isValueReplayMap)
    {
        _execution = execution;
        RuntimeIdentity = RenderDescriptionValidation.ResolveRuntimeIdentity(execution);
        Bounds = bounds;
        HitTest = hitTest;
        Scale = scale;
        DeviceGridMapping = deviceGridMapping;
        StructuralKey = structuralKey;
        Resources = resources;
        IsValueReplayMap = isValueReplayMap;
    }

    public RenderBoundsContract Bounds { get; }

    public RenderHitTestContract HitTest { get; }

    public RenderScaleContract Scale { get; }

    /// <summary>Gets the declared device pixel grid this scope replays its input onto.</summary>
    public RenderDeviceGridMapping DeviceGridMapping { get; }

    public object StructuralKey { get; }

    public RenderRuntimeIdentity? RuntimeIdentity { get; }

    public IReadOnlyList<RenderResource> Resources { get; }

    internal void Execute(TargetScopeSession session) => _execution.Invoke(session);

    internal bool IsValueReplayMap { get; }

    /// <param name="state">
    /// Every pixel-affecting value the callback reads, and the complete output-cache runtime identity of the
    /// scope. It must be a lightweight immutable CPU value.
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
    public static TargetScopeDescription Create<TState>(
        TState state,
        Action<TargetScopeSession, TState> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridMapping deviceGridMapping = RenderDeviceGridMapping.Remapped,
        object? structuralKey = null,
        IEnumerable<RenderResource>? resources = null)
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
            deviceGridMapping,
            structuralKey,
            resources,
            isValueReplayMap: false);

    /// <summary>
    /// Creates a scope whose output can never satisfy a later request's cache lookup.
    /// </summary>
    /// <remarks>
    /// The opt-out for a callback whose pixel-affecting state cannot be expressed as a lightweight immutable
    /// key. The callback may capture, and the recorded output takes a fresh request-local identity every time.
    /// </remarks>
    public static TargetScopeDescription CreateRequestLocal(
        Action<TargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridMapping deviceGridMapping = RenderDeviceGridMapping.Remapped,
        object? structuralKey = null,
        IEnumerable<RenderResource>? resources = null)
        => CreateCore(
            RenderDescriptionValidation.CreateRequestLocalChannel(execute, nameof(execute)),
            bounds,
            hitTest,
            scale,
            deviceGridMapping,
            structuralKey,
            resources,
            isValueReplayMap: false);

    /// <summary>
    /// Creates a scope the renderer lowers into the value graph instead of materializing its input.
    /// </summary>
    /// <remarks>
    /// Eligibility is engine-owned because no declaration can establish it: the callback must be mechanically
    /// restricted to allocation-free target state plus exactly one replay, which only an in-engine author can
    /// guarantee. Public <see cref="Create"/> therefore always produces a materializing boundary.
    /// </remarks>
    internal static TargetScopeDescription CreateValueReplayMap(
        Action<TargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridMapping deviceGridMapping,
        object structuralKey,
        RenderRuntimeIdentity? runtimeIdentity = null,
        IEnumerable<RenderResource>? resources = null)
        => CreateCore(
            RenderDescriptionValidation.CreateDeclaredIdentityChannel(
                execute,
                runtimeIdentity,
                nameof(execute),
                nameof(runtimeIdentity)),
            bounds,
            hitTest,
            scale,
            deviceGridMapping,
            structuralKey,
            resources,
            isValueReplayMap: true);

    private static TargetScopeDescription CreateCore(
        RenderExecutionChannel<TargetScopeSession> execution,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridMapping deviceGridMapping,
        object? structuralKey,
        IEnumerable<RenderResource>? resources,
        bool isValueReplayMap)
    {
        bounds.ThrowIfUninitialized(nameof(bounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        if (!Enum.IsDefined(deviceGridMapping))
            throw new ArgumentOutOfRangeException(nameof(deviceGridMapping));

        return new TargetScopeDescription(
            execution,
            bounds,
            hitTest,
            scale,
            deviceGridMapping,
            RenderDescriptionValidation.ResolveStructuralKey(
                structuralKey,
                execution.Method,
                nameof(structuralKey)),
            RenderDescriptionValidation.CopyResources(resources, nameof(resources)),
            isValueReplayMap);
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
        IReadOnlyList<RenderResource> resources,
        Action<ImmediateCanvas> replayInput)
    {
        _token = token;
        _outputBounds = outputBounds;
        _requiredRegion = requiredRegion;
        _intent = intent;
        _purpose = purpose;
        _canvas = canvas;
        _resources = resources;
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

    /// <summary>Uses a resource by its token.</summary>
    /// <remarks>
    /// The addressing mode for a callback that may capture: one recorded through <c>CreateRequestLocal</c>, or
    /// one whose runtime identity is declared separately from what it captures. A state-passing callback
    /// addresses its resources through <c>UseDeclaredResource</c> instead, because its state is the
    /// output-cache runtime identity and the state walk rejects a <see cref="RenderResource"/> element. Smuggling
    /// a token in through a sealed non-tuple state object still reaches this method, at the price of a runtime
    /// identity that differs every frame and never matches a cached output.
    /// </remarks>
    public void UseResource<T>(RenderResource<T> resource, Action<T> use)
        where T : class
    {
        _token.UseResource(resource, _resources, use);
    }

    /// <summary>Uses a resource by its position in the description's declared resource list.</summary>
    /// <remarks>
    /// The addressing mode a non-capturing callback needs: a resource token is request-scoped and can never be
    /// part of a persistent identity, so it cannot travel through the description's state. The position is the
    /// only address, and <typeparamref name="T"/> is the only check on it: two declared resources of the same
    /// type make index 0 and index 1 indistinguishable, so prepending or reordering <c>resources</c> silently
    /// swaps which one this call reaches.
    /// </remarks>
    public void UseDeclaredResource<T>(int declaredIndex, Action<T> use)
        where T : class
    {
        _token.UseDeclaredResource(declaredIndex, _resources, use);
    }

    internal void ValidateCompletion()
    {
        _token.ThrowIfInactive();
        if (!_replayed)
            throw new InvalidOperationException("A target scope input must be replayed exactly once.");
    }
}

public sealed class RawTargetScopeDescription
{
    private readonly RenderExecutionChannel<RawTargetScopeSession> _execution;

    private RawTargetScopeDescription(
        RenderExecutionChannel<RawTargetScopeSession> execution,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        object structuralKey,
        IReadOnlyList<RenderResource> resources)
    {
        _execution = execution;
        Bounds = bounds;
        HitTest = hitTest;
        Scale = scale;
        StructuralKey = structuralKey;
        Resources = resources;
    }

    public RenderBoundsContract Bounds { get; }

    public RenderHitTestContract HitTest { get; }

    public RenderScaleContract Scale { get; }

    public object StructuralKey { get; }

    public IReadOnlyList<RenderResource> Resources { get; }

    internal void Execute(RawTargetScopeSession session) => _execution.Invoke(session);

    /// <summary>
    /// Creates a raw scope whose output can never satisfy a later request's cache lookup.
    /// </summary>
    /// <remarks>
    /// A raw scope hands an unguarded canvas to an opaque external callback, so the renderer can describe
    /// nothing about what it draws and gives every recording a fresh request-local identity. There is no
    /// state-passing form: no declared state could make the output reusable.
    /// </remarks>
    public static RawTargetScopeDescription CreateRequestLocal(
        Action<RawTargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        object? structuralKey = null,
        IEnumerable<RenderResource>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        bounds.ThrowIfUninitialized(nameof(bounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));

        return new RawTargetScopeDescription(
            RenderDescriptionValidation.CreateRequestLocalChannel(execute, nameof(execute)),
            bounds,
            hitTest,
            scale,
            RenderDescriptionValidation.ResolveStructuralKey(
                structuralKey,
                execute.Method,
                nameof(structuralKey)),
            RenderDescriptionValidation.CopyResources(resources, nameof(resources)));
    }
}

public sealed class RawTargetScopeSession
{
    private readonly RenderExecutionSessionToken _token;
    private readonly ImmediateCanvas _canvas;
    private readonly Rect _outputBounds;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly IReadOnlyList<RenderResource> _resources;
    private readonly Action<ImmediateCanvas> _replayInput;
    private bool _replayed;

    internal RawTargetScopeSession(
        RenderExecutionSessionToken token,
        ImmediateCanvas canvas,
        Rect outputBounds,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        IReadOnlyList<RenderResource> resources,
        Action<ImmediateCanvas> replayInput)
    {
        _token = token;
        _canvas = canvas;
        _outputBounds = outputBounds;
        _intent = intent;
        _purpose = purpose;
        _resources = resources;
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

    /// <summary>Uses a resource by its token.</summary>
    /// <remarks>
    /// The only addressing mode here. An unguarded external callback is never reusable, so it is recorded
    /// through <c>CreateRequestLocal</c> only and may always capture the tokens it needs.
    /// </remarks>
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
        object structuralKey,
        IReadOnlyList<RenderResource> resources)
    {
        _execution = execution;
        QueryBounds = queryBounds;
        HitTest = hitTest;
        StructuralKey = structuralKey;
        Resources = resources;
    }

    public Rect QueryBounds { get; }

    public RenderHitTestContract HitTest { get; }

    public object StructuralKey { get; }

    public IReadOnlyList<RenderResource> Resources { get; }

    internal void Execute(RawTargetCommandSession session) => _execution.Invoke(session);

    /// <summary>
    /// Creates a raw command whose effect on the target can never satisfy a later request's cache lookup.
    /// </summary>
    /// <remarks>
    /// A raw command hands an unguarded canvas to an opaque external callback, so the renderer can describe
    /// nothing about what it draws and gives every recording a fresh request-local identity. There is no
    /// state-passing form: no declared state could make the output reusable.
    /// </remarks>
    public static RawTargetCommandDescription CreateRequestLocal(
        Action<RawTargetCommandSession> execute,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        object? structuralKey = null,
        IEnumerable<RenderResource>? resources = null)
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

        return new RawTargetCommandDescription(
            RenderDescriptionValidation.CreateRequestLocalChannel(execute, nameof(execute)),
            queryBounds,
            hitTest,
            RenderDescriptionValidation.ResolveStructuralKey(
                structuralKey,
                execute.Method,
                nameof(structuralKey)),
            RenderDescriptionValidation.CopyResources(resources, nameof(resources)));
    }
}

public sealed class RawTargetCommandSession
{
    private readonly RenderExecutionSessionToken _token;
    private readonly ImmediateCanvas _canvas;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly IReadOnlyList<RenderResource> _resources;

    internal RawTargetCommandSession(
        RenderExecutionSessionToken token,
        ImmediateCanvas canvas,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        IReadOnlyList<RenderResource> resources)
    {
        _token = token;
        _canvas = canvas;
        _intent = intent;
        _purpose = purpose;
        _resources = resources;
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

    /// <summary>Uses a resource by its token.</summary>
    /// <remarks>
    /// The only addressing mode here. An unguarded external callback is never reusable, so it is recorded
    /// through <c>CreateRequestLocal</c> only and may always capture the tokens it needs.
    /// </remarks>
    public void UseResource<T>(RenderResource<T> resource, Action<T> use)
        where T : class
    {
        _token.UseResource(resource, _resources, use);
    }
}
