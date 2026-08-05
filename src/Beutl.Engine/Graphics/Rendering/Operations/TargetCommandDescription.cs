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
        IReadOnlyList<RenderInputReadback> inputReadbacks,
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

    public IReadOnlyList<RenderInputReadback> InputReadbacks { get; }

    public object StructuralKey { get; }

    public RenderRuntimeIdentity? RuntimeIdentity { get; }

    public IReadOnlyList<RenderResource> Resources { get; }

    internal Action<TargetCommandSession> Execute { get; }

    /// <param name="access">
    /// <see cref="TargetAccess.Readback"/> obliges the callback to consume
    /// <see cref="TargetCommandSession.UseSnapshot"/> exactly once.
    /// </param>
    public static TargetCommandDescription Create(
        Action<TargetCommandSession> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access = TargetAccess.ReadWrite,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        object? structuralKey = null,
        RenderRuntimeIdentity? runtimeIdentity = null,
        IEnumerable<RenderResource>? resources = null)
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
            execute.Method,
            nameof(structuralKey));
        RenderDescriptionValidation.ValidateRuntimeIdentity(runtimeIdentity, nameof(runtimeIdentity));
        RenderInputReadback[] readbacks = CopyInputReadbacks(inputReadbacks);

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
