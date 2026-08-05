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
    private TargetScopeDescription(
        Action<TargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridMapping deviceGridMapping,
        object structuralKey,
        RenderRuntimeIdentity? runtimeIdentity,
        IReadOnlyList<RenderResource> resources,
        bool isValueReplayMap)
    {
        Execute = execute;
        Bounds = bounds;
        HitTest = hitTest;
        Scale = scale;
        DeviceGridMapping = deviceGridMapping;
        StructuralKey = structuralKey;
        RuntimeIdentity = runtimeIdentity;
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

    internal Action<TargetScopeSession> Execute { get; }

    internal bool IsValueReplayMap { get; }

    /// <param name="deviceGridMapping">
    /// The device pixel grid the callback replays its input onto. The default assumes a different grid;
    /// declare <see cref="RenderDeviceGridMapping.Preserved"/> only when the callback leaves the target
    /// transform alone.
    /// </param>
    public static TargetScopeDescription Create(
        Action<TargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridMapping deviceGridMapping = RenderDeviceGridMapping.Remapped,
        object? structuralKey = null,
        RenderRuntimeIdentity? runtimeIdentity = null,
        IEnumerable<RenderResource>? resources = null)
        => CreateCore(
            execute,
            bounds,
            hitTest,
            scale,
            deviceGridMapping,
            structuralKey,
            runtimeIdentity,
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
            execute,
            bounds,
            hitTest,
            scale,
            deviceGridMapping,
            structuralKey,
            runtimeIdentity,
            resources,
            isValueReplayMap: true);

    private static TargetScopeDescription CreateCore(
        Action<TargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridMapping deviceGridMapping,
        object? structuralKey,
        RenderRuntimeIdentity? runtimeIdentity,
        IEnumerable<RenderResource>? resources,
        bool isValueReplayMap)
    {
        ArgumentNullException.ThrowIfNull(execute);
        bounds.ThrowIfUninitialized(nameof(bounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        if (!Enum.IsDefined(deviceGridMapping))
            throw new ArgumentOutOfRangeException(nameof(deviceGridMapping));
        RenderDescriptionValidation.ValidateRuntimeIdentity(runtimeIdentity, nameof(runtimeIdentity));

        return new TargetScopeDescription(
            execute,
            bounds,
            hitTest,
            scale,
            deviceGridMapping,
            RenderDescriptionValidation.ResolveStructuralKey(
                structuralKey,
                execute.Method,
                nameof(structuralKey)),
            runtimeIdentity,
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

    public void UseResource<T>(RenderResource<T> resource, Action<T> use)
        where T : class
    {
        _token.UseResource(resource, _resources, use);
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
    private RawTargetScopeDescription(
        Action<RawTargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        object structuralKey,
        IReadOnlyList<RenderResource> resources)
    {
        Execute = execute;
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

    internal Action<RawTargetScopeSession> Execute { get; }

    public static RawTargetScopeDescription Create(
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
            execute,
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
    private RawTargetCommandDescription(
        Action<RawTargetCommandSession> execute,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        object structuralKey,
        IReadOnlyList<RenderResource> resources)
    {
        Execute = execute;
        QueryBounds = queryBounds;
        HitTest = hitTest;
        StructuralKey = structuralKey;
        Resources = resources;
    }

    public Rect QueryBounds { get; }

    public RenderHitTestContract HitTest { get; }

    public object StructuralKey { get; }

    public IReadOnlyList<RenderResource> Resources { get; }

    internal Action<RawTargetCommandSession> Execute { get; }

    public static RawTargetCommandDescription Create(
        Action<RawTargetCommandSession> execute,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        object? structuralKey = null,
        IEnumerable<RenderResource>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        RenderRectValidation.ThrowIfInvalidInput(queryBounds, nameof(queryBounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));

        return new RawTargetCommandDescription(
            execute,
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

    public void UseResource<T>(RenderResource<T> resource, Action<T> use)
        where T : class
    {
        _token.UseResource(resource, _resources, use);
    }
}
