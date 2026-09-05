namespace Beutl.Graphics.Rendering;

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

    /// <summary>Creates a raw target-command description.</summary>
    /// <param name="state">
    /// Immutable pixel-affecting state retained for execution.
    /// </param>
    /// <param name="execute">
    /// A static execution callback.
    /// </param>
    /// <remarks>
    /// Raw commands publish no cacheable value. Static execution gives repeated recordings one plan identity.
    /// </remarks>
    public static RawTargetCommandDescription Create<TState>(
        TState state,
        Action<RawTargetCommandSession, TState> execute,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        IReadOnlyList<RenderResourceBinding>? resources = null)
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
            resources);

    /// <summary>
    /// Creates a raw command whose recorded work can never satisfy a later request's plan lookup.
    /// </summary>
    /// <inheritdoc cref="RawTargetScopeDescription.CreateRequestLocal" path="/remarks"/>
    internal static RawTargetCommandDescription CreateRequestLocal(
        Action<RawTargetCommandSession> execute,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        IReadOnlyList<RenderResourceBinding>? resources = null)
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
        IReadOnlyList<RenderResourceBinding>? resources)
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
