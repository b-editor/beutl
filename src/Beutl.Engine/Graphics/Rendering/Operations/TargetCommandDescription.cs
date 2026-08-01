using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class TargetCommandDescription
{
    private TargetCommandDescription(
        Action<TargetCommandSession> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access,
        IReadOnlyList<TargetInputReadback> inputReadbacks,
        object structuralKey,
        RenderRuntimeIdentity? runtimeIdentity,
        IReadOnlyList<RenderResource> resources)
    {
        Execute = execute;
        AffectedRegion = affectedRegion;
        QueryBounds = queryBounds;
        HitTest = hitTest;
        Access = access;
        InputReadbacks = inputReadbacks;
        StructuralKey = structuralKey;
        RuntimeIdentity = runtimeIdentity;
        Resources = resources;
    }

    public TargetRegion AffectedRegion { get; }

    public Rect QueryBounds { get; }

    public RenderHitTestContract HitTest { get; }

    public TargetAccess Access { get; }

    public IReadOnlyList<TargetInputReadback> InputReadbacks { get; }

    public object StructuralKey { get; }

    public RenderRuntimeIdentity? RuntimeIdentity { get; }

    public IReadOnlyList<RenderResource> Resources { get; }

    internal Action<TargetCommandSession> Execute { get; }

    public static TargetCommandDescription Create(
        Action<TargetCommandSession> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access,
        IEnumerable<TargetInputReadback>? inputReadbacks = null,
        object? structuralKey = null,
        RenderRuntimeIdentity? runtimeIdentity = null,
        IEnumerable<RenderResource>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        affectedRegion.ThrowIfUninitialized(nameof(affectedRegion));
        RenderRectValidation.ThrowIfInvalidInput(queryBounds, nameof(queryBounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        if (!Enum.IsDefined(access))
            throw new ArgumentOutOfRangeException(nameof(access), access, "The target access value is invalid.");
        if (access == TargetAccess.Readback && affectedRegion.Kind == TargetRegionKind.Empty)
        {
            throw new ArgumentException(
                "A readback command requires a non-empty target region.",
                nameof(affectedRegion));
        }

        object resolvedStructuralKey = structuralKey is null
            ? new TargetCommandStructuralIdentity(execute.Method, access)
            : RenderDescriptionValidation.ResolveStructuralKey(
                structuralKey,
                execute.Method,
                nameof(structuralKey));
        RenderDescriptionValidation.ValidateRuntimeIdentity(runtimeIdentity, nameof(runtimeIdentity));
        TargetInputReadback[] readbacks = CopyInputReadbacks(inputReadbacks);

        return new TargetCommandDescription(
            execute,
            affectedRegion,
            queryBounds,
            hitTest,
            access,
            Array.AsReadOnly(readbacks),
            resolvedStructuralKey,
            runtimeIdentity,
            RenderDescriptionValidation.CopyResources(resources, nameof(resources)));
    }

    internal IReadOnlyList<TargetInputReadback> ResolveInputReadbacks(
        int inputCount,
        string parameterName)
    {
        if (InputReadbacks.Count == 0)
            return Enumerable.Repeat(TargetInputReadback.None, inputCount).ToArray();
        if (InputReadbacks.Count != inputCount)
        {
            throw new ArgumentException(
                "The target-command input readback count must match the authored input count.",
                parameterName);
        }
        return InputReadbacks;
    }

    private static TargetInputReadback[] CopyInputReadbacks(
        IEnumerable<TargetInputReadback>? inputReadbacks)
    {
        if (inputReadbacks is null)
            return [];

        TargetInputReadback[] result = inputReadbacks.ToArray();
        foreach (TargetInputReadback inputReadback in result)
        {
            inputReadback.ThrowIfUninitialized(nameof(inputReadbacks));
        }

        return result;
    }
}

public enum TargetAccess
{
    ReadWrite,
    Readback,
}

public sealed class TargetCommandSession
{
    private readonly RenderExecutionSessionToken _token;
    private readonly IReadOnlyList<RenderExecutionInput> _inputs;
    private readonly Rect _affectedBounds;
    private readonly Rect _requiredRegion;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;
    private readonly RenderCallbackCanvas _canvas;
    private readonly IReadOnlyList<RenderResource> _resources;
    private readonly Func<Bitmap>? _createSnapshot;
    private readonly bool _snapshotRequired;
    private bool _snapshotUsed;

    internal TargetCommandSession(
        RenderExecutionSessionToken token,
        IReadOnlyList<RenderExecutionInput> inputs,
        Rect affectedBounds,
        Rect requiredRegion,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        RenderCallbackCanvas canvas,
        IReadOnlyList<RenderResource> resources,
        bool snapshotRequired,
        Func<Bitmap>? createSnapshot)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(resources);
        _token = token;
        _inputs = Array.AsReadOnly(inputs.ToArray());
        _affectedBounds = affectedBounds;
        _requiredRegion = requiredRegion;
        _intent = intent;
        _purpose = purpose;
        _canvas = canvas;
        _resources = resources;
        _snapshotRequired = snapshotRequired;
        _createSnapshot = createSnapshot;
    }

    public IReadOnlyList<RenderExecutionInput> Inputs
    {
        get { _token.ThrowIfInactive(); return _inputs; }
    }

    public Rect AffectedBounds
    {
        get { _token.ThrowIfInactive(); return _affectedBounds; }
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

    public void UseSnapshot(Action<Bitmap> use)
    {
        _token.ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(use);
        if (!_snapshotRequired || _createSnapshot is null)
            throw new InvalidOperationException("This target command did not declare target readback.");
        if (_snapshotUsed)
            throw new InvalidOperationException("The target snapshot is a one-shot execution lease.");

        _snapshotUsed = true;
        using Bitmap snapshot = _createSnapshot()
            ?? throw new InvalidOperationException("The target snapshot provider returned null.");
        _token.AuthorizeResource(snapshot, () => use(snapshot));
    }

    public void UseResource<T>(RenderResource<T> resource, Action<T> use)
        where T : class
    {
        _token.UseResource(resource, _resources, use);
    }

    internal void ValidateCompletion()
    {
        _token.ThrowIfInactive();
        if (_snapshotRequired && !_snapshotUsed)
            throw new InvalidOperationException("A readback target command must consume its snapshot exactly once.");
    }
}

internal readonly record struct TargetCommandStructuralIdentity(
    System.Reflection.MethodInfo CallbackMethod,
    TargetAccess Access);
