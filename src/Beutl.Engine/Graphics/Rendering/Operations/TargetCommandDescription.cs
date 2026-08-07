using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class TargetCommandDescription
{
    private readonly RenderExecutionChannel<TargetCommandSession> _execution;

    private TargetCommandDescription(
        RenderExecutionChannel<TargetCommandSession> execution,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access,
        IReadOnlyList<RenderInputReadback> inputReadbacks,
        object structuralKey,
        IReadOnlyList<RenderResource> resources)
    {
        _execution = execution;
        RuntimeIdentity = RenderDescriptionValidation.ResolveRuntimeIdentity(execution);
        AffectedRegion = affectedRegion;
        QueryBounds = queryBounds;
        HitTest = hitTest;
        Access = access;
        InputReadbacks = inputReadbacks;
        StructuralKey = structuralKey;
        Resources = resources;
    }

    public TargetRegion AffectedRegion { get; }

    public Rect QueryBounds { get; }

    public RenderHitTestContract HitTest { get; }

    public TargetAccess Access { get; }

    public IReadOnlyList<RenderInputReadback> InputReadbacks { get; }

    public object StructuralKey { get; }

    public RenderRuntimeIdentity? RuntimeIdentity { get; }

    public IReadOnlyList<RenderResource> Resources { get; }

    internal void Execute(TargetCommandSession session) => _execution.Invoke(session);

    /// <param name="state">
    /// Every pixel-affecting value the callback reads, and the complete output-cache runtime identity of the
    /// command. It must be a lightweight immutable CPU value.
    /// </param>
    /// <param name="execute">
    /// A non-capturing callback. Declare it <see langword="static"/>: a capture would let a per-frame value
    /// shape the target without reaching <paramref name="state"/>, and is rejected.
    /// </param>
    /// <param name="access">
    /// <see cref="TargetAccess.Readback"/> obliges the callback to consume
    /// <see cref="TargetCommandSession.UseSnapshot"/> exactly once.
    /// </param>
    public static TargetCommandDescription Create<TState>(
        TState state,
        Action<TargetCommandSession, TState> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access = TargetAccess.ReadWrite,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        object? structuralKey = null,
        IEnumerable<RenderResource>? resources = null)
        where TState : notnull
        => CreateCore(
            RenderDescriptionValidation.CreateStateChannel(
                state,
                execute,
                nameof(state),
                nameof(execute)),
            affectedRegion,
            queryBounds,
            hitTest,
            access,
            inputReadbacks,
            structuralKey,
            resources);

    /// <summary>
    /// Creates a command whose effect on the target can never satisfy a later request's cache lookup.
    /// </summary>
    /// <remarks>
    /// The opt-out for a callback whose pixel-affecting state cannot be expressed as a lightweight immutable
    /// key. The callback may capture, and the recorded output takes a fresh request-local identity every time.
    /// </remarks>
    public static TargetCommandDescription CreateRequestLocal(
        Action<TargetCommandSession> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access = TargetAccess.ReadWrite,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        object? structuralKey = null,
        IEnumerable<RenderResource>? resources = null)
        => CreateCore(
            RenderDescriptionValidation.CreateRequestLocalChannel(execute, nameof(execute)),
            affectedRegion,
            queryBounds,
            hitTest,
            access,
            inputReadbacks,
            structuralKey,
            resources);

    private static TargetCommandDescription CreateCore(
        RenderExecutionChannel<TargetCommandSession> execution,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access,
        IEnumerable<RenderInputReadback>? inputReadbacks,
        object? structuralKey,
        IEnumerable<RenderResource>? resources)
    {
        affectedRegion.ThrowIfUninitialized(nameof(affectedRegion));
        RenderRectValidation.ThrowIfInvalidInput(queryBounds, nameof(queryBounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        RenderDescriptionValidation.ThrowIfQueryContributionIncoherent(
            queryBounds,
            hitTest,
            nameof(hitTest));
        if (!Enum.IsDefined(access))
            throw new ArgumentOutOfRangeException(nameof(access), access, "The target access value is invalid.");
        if (access == TargetAccess.Readback && affectedRegion.Kind == TargetRegionKind.Empty)
        {
            throw new ArgumentException(
                "A readback command requires a non-empty target region.",
                nameof(affectedRegion));
        }

        // Access is its own component of both the structural plan key and the output-cache identity, so the
        // default key stays the bare callback method and allocates nothing.
        object resolvedStructuralKey = RenderDescriptionValidation.ResolveStructuralKey(
            structuralKey,
            execution.Method,
            nameof(structuralKey));
        RenderInputReadback[] readbacks = CopyInputReadbacks(inputReadbacks);

        return new TargetCommandDescription(
            execution,
            affectedRegion,
            queryBounds,
            hitTest,
            access,
            Array.AsReadOnly(readbacks),
            resolvedStructuralKey,
            RenderDescriptionValidation.CopyResources(resources, nameof(resources)));
    }

    internal IReadOnlyList<RenderInputReadback> ResolveInputReadbacks(
        int inputCount,
        string parameterName)
    {
        if (InputReadbacks.Count == 0)
            return Enumerable.Repeat(RenderInputReadback.None, inputCount).ToArray();
        if (InputReadbacks.Count != inputCount)
        {
            throw new ArgumentException(
                "The target-command input readback count must match the authored input count.",
                parameterName);
        }
        return InputReadbacks;
    }

    private static RenderInputReadback[] CopyInputReadbacks(
        IEnumerable<RenderInputReadback>? inputReadbacks)
    {
        if (inputReadbacks is null)
            return [];

        RenderInputReadback[] result = inputReadbacks.ToArray();
        foreach (RenderInputReadback inputReadback in result)
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
    private readonly IReadOnlyList<RenderExecutionInputRange> _inputRanges;
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
        IReadOnlyList<RenderExecutionInputRange> inputRanges,
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
        ArgumentNullException.ThrowIfNull(inputRanges);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(resources);
        _token = token;
        _inputs = Array.AsReadOnly(inputs.ToArray());
        _inputRanges = RenderExecutionInputRange.CopyAndValidate(
            _inputs,
            inputRanges,
            nameof(inputRanges));
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

    /// <summary>
    /// Gets one stable flattened-input range per authored input handle, including zero-length ranges for handles
    /// that produced no runtime values.
    /// </summary>
    public IReadOnlyList<RenderExecutionInputRange> InputRanges
    {
        get { _token.ThrowIfInactive(); return _inputRanges; }
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

    /// <summary>Replaces every pixel in the declared affected region with <paramref name="color"/>.</summary>
    /// <remarks>
    /// The operation uses clipped source replacement, so a transparent color erases a finite region without
    /// exposing unrestricted blend state or writing outside the command's declared target access.
    /// </remarks>
    public void ReplaceAffectedRegion(Color color)
    {
        _token.ThrowIfInactive();
        _canvas.Use(canvas => canvas.ReplaceAffectedRegion(color));
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

    /// <summary>Uses a resource by its token.</summary>
    /// <remarks>
    /// The addressing mode for a callback that may capture: one recorded through <c>CreateRequestLocal</c>, or
    /// one whose runtime identity is declared separately from what it captures. A state-passing callback
    /// addresses its resources through <c>UseDeclaredResource</c> instead, because its state is the produced
    /// value's output-cache runtime identity: a <see cref="RenderResource"/> in a tuple element is rejected, and
    /// so is a capturing callback. A sealed non-tuple state does pass validation and physically delivers a token
    /// to this method, but it is an enumerated identity channel rather than a way to address resources — the
    /// author then owns the identity contract by hand. A holder allocated per recording loses output-cache
    /// reuse unless it defines value equality. A holder reused and mutated in place keeps reuse but cannot be
    /// an identity at all: the cache stores the reference, mutation moves both sides of the comparison at once,
    /// and <see cref="object.Equals(object, object)"/> returns on reference equality before reaching the
    /// author's override — so a pixel-affecting change only that holder carries is served from a stale cached
    /// output. A token left over from a finished request throws when leased. Position is the address by design,
    /// not by impossibility.
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
        if (_snapshotRequired && !_snapshotUsed)
            throw new InvalidOperationException("A readback target command must consume its snapshot exactly once.");
    }
}
