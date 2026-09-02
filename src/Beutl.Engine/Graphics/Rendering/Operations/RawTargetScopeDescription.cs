namespace Beutl.Graphics.Rendering;

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

    /// <summary>Creates a raw target-scope description.</summary>
    /// <param name="state">
    /// Immutable pixel-affecting state retained for execution.
    /// </param>
    /// <param name="execute">
    /// A static execution callback.
    /// </param>
    /// <param name="slots">
    /// Declared slots. <paramref name="resources"/> must bind each exactly once.
    /// </param>
    /// <remarks>
    /// Raw work is not output-cacheable. Static execution still gives repeated recordings one plan identity.
    /// </remarks>
    public static RawTargetScopeDescription Create<TState>(
        TState state,
        Action<RawTargetScopeSession, TState> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        IReadOnlyList<RenderResourceBinding>? resources = null,
        IReadOnlyList<RenderResourceSlot>? slots = null)
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
        IReadOnlyList<RenderResourceBinding>? resources = null)
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
        IReadOnlyList<RenderResourceBinding>? resources)
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
