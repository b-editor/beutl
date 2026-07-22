namespace Beutl.Graphics.Rendering;

public sealed class TargetScopeDescription
{
    private TargetScopeDescription(
        Action<TargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        object structuralKey,
        RenderRuntimeIdentity? runtimeIdentity,
        IReadOnlyList<RenderResource> resources,
        bool isValueReplayMap)
    {
        Execute = execute;
        Bounds = bounds;
        HitTest = hitTest;
        Scale = scale;
        StructuralKey = structuralKey;
        RuntimeIdentity = runtimeIdentity;
        Resources = resources;
        IsValueReplayMap = isValueReplayMap;
    }

    public RenderBoundsContract Bounds { get; }

    public RenderHitTestContract HitTest { get; }

    public RenderScaleContract Scale { get; }

    public object StructuralKey { get; }

    public RenderRuntimeIdentity? RuntimeIdentity { get; }

    public IReadOnlyList<RenderResource> Resources { get; }

    internal Action<TargetScopeSession> Execute { get; }

    internal bool IsValueReplayMap { get; }

    public static TargetScopeDescription Create(
        Action<TargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        object? structuralKey = null,
        RenderRuntimeIdentity? runtimeIdentity = null,
        IEnumerable<RenderResource>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        bounds.ThrowIfUninitialized(nameof(bounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        RenderDescriptionValidation.ValidateRuntimeIdentity(runtimeIdentity, nameof(runtimeIdentity));

        return new TargetScopeDescription(
            execute,
            bounds,
            hitTest,
            scale,
            RenderDescriptionValidation.ResolveStructuralKey(
                structuralKey,
                execute.Method,
                nameof(structuralKey)),
            runtimeIdentity,
            RenderDescriptionValidation.CopyResources(resources, nameof(resources)),
            isValueReplayMap: false);
    }

    internal static TargetScopeDescription CreateValueReplayMap(
        Action<TargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        object structuralKey,
        RenderRuntimeIdentity? runtimeIdentity = null,
        IEnumerable<RenderResource>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        bounds.ThrowIfUninitialized(nameof(bounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        RenderDescriptionValidation.ValidateRuntimeIdentity(runtimeIdentity, nameof(runtimeIdentity));

        return new TargetScopeDescription(
            execute,
            bounds,
            hitTest,
            scale,
            RenderDescriptionValidation.ResolveStructuralKey(
                structuralKey,
                execute.Method,
                nameof(structuralKey)),
            runtimeIdentity,
            RenderDescriptionValidation.CopyResources(resources, nameof(resources)),
            isValueReplayMap: true);
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
