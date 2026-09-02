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
        object definitionFingerprint,
        RenderInputDemandContract inputDemand,
        IReadOnlyList<RenderResourceBinding> resources)
    {
        _execution = execution;
        AffectedRegion = affectedRegion;
        QueryBounds = queryBounds;
        HitTest = hitTest;
        Access = access;
        InputReadbacks = inputReadbacks;
        DefinitionFingerprint = definitionFingerprint;
        InputDemand = inputDemand;
        Resources = resources;
    }

    /// <summary>Gets the mapping from this command's target demand to the demand it places on each input.</summary>
    /// <remarks>
    /// A command that resamples an input while drawing it - a transform pushed before
    /// <c>Inputs[i].Draw</c> - needs that input at a different density from the target it draws onto.
    /// </remarks>
    public RenderInputDemandContract InputDemand { get; }

    public TargetRegion AffectedRegion { get; }

    public Rect QueryBounds { get; }

    public RenderHitTestContract HitTest { get; }

    public TargetAccess Access { get; }

    public IReadOnlyList<RenderInputReadback> InputReadbacks { get; }

    internal object DefinitionFingerprint { get; }

    public IReadOnlyList<RenderResourceBinding> Resources { get; }

    internal void Execute(TargetCommandSession session) => _execution.Invoke(session);

    /// <param name="state">
    /// Immutable pixel-affecting state retained for execution.
    /// </param>
    /// <param name="execute">
    /// A static execution callback.
    /// </param>
    /// <param name="access">
    /// <see cref="TargetAccess.Readback"/> obliges the callback to consume
    /// <see cref="TargetCommandSession.UseSnapshot"/> exactly once.
    /// </param>
    /// <param name="inputDemand">
    /// Per-input density required for the command's resolved output demand.
    /// </param>
    /// <param name="slots">
    /// Declared slots. <paramref name="resources"/> must bind each exactly once.
    /// </param>
    public static TargetCommandDescription Create<TState>(
        TState state,
        Action<TargetCommandSession, TState> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access = TargetAccess.ReadWrite,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IReadOnlyList<RenderResourceBinding>? resources = null,
        RenderInputDemandContract inputDemand = default,
        IReadOnlyList<RenderResourceSlot>? slots = null)
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
            RenderDescriptionValidation.StructuralIdentityOfExecution(execute),
            inputDemand,
            RenderDescriptionValidation.BindDeclaredSlots(
                slots,
                resources,
                nameof(slots),
                nameof(resources)));

    /// <summary>
    /// Creates a command whose effect on the target can never satisfy a later request's cache lookup.
    /// </summary>
    /// <remarks>
    /// The opt-out for a callback whose pixel-affecting state cannot be expressed as copied, deeply immutable
    /// CPU state. The callback may capture, and the recorded output takes a fresh request-local identity every
    /// time.
    /// </remarks>
    internal static TargetCommandDescription CreateRequestLocal(
        Action<TargetCommandSession> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access = TargetAccess.ReadWrite,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IReadOnlyList<RenderResourceBinding>? resources = null,
        RenderInputDemandContract inputDemand = default)
        => CreateCore(
            RenderDescriptionValidation.CreateRequestLocalChannel(execute, nameof(execute)),
            affectedRegion,
            queryBounds,
            hitTest,
            access,
            inputReadbacks,
            execute,
            inputDemand,
            resources);

    internal static TargetCommandDescription CreateCore(
        RenderExecutionChannel<TargetCommandSession> execution,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access,
        IEnumerable<RenderInputReadback>? inputReadbacks,
        object definitionFingerprint,
        RenderInputDemandContract inputDemand,
        IReadOnlyList<RenderResourceBinding>? resources)
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

        ArgumentNullException.ThrowIfNull(definitionFingerprint);
        RenderInputReadback[] readbacks = CopyInputReadbacks(inputReadbacks);

        return new TargetCommandDescription(
            execution,
            affectedRegion,
            queryBounds,
            hitTest,
            access,
            Array.AsReadOnly(readbacks),
            definitionFingerprint,
            inputDemand,
            RenderDescriptionValidation.CopyResourceBindings(resources, nameof(resources)));
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
