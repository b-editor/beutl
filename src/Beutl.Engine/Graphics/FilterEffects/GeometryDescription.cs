using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

/// <summary>Declares an immutable deferred geometry transformation recorded into a render graph.</summary>
/// <remarks>
/// Geometry is an order-preserving zero-or-one map over each input value and is a materialization boundary.
/// The renderer derives plan shape from the callback and declared contracts. The render callback receives a
/// borrowed execution-scoped <see cref="GeometrySession"/> that must not be retained.
/// </remarks>
public sealed class GeometryDescription
{
    private readonly RenderExecutionChannel<GeometrySession> _execution;

    private GeometryDescription(
        RenderExecutionChannel<GeometrySession> execution,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        object definitionFingerprint,
        bool requiresReadback,
        RenderInputDemandContract inputDemand,
        IReadOnlyList<RenderResourceBinding> resources)
    {
        _execution = execution;
        Bounds = bounds;
        HitTest = hitTest;
        DefinitionFingerprint = definitionFingerprint;
        RequiresReadback = requiresReadback;
        InputDemand = inputDemand;
        Resources = resources;
        StructuralIdentity = new GeometryStructuralIdentity(
            definitionFingerprint,
            bounds.StructuralIdentity,
            hitTest.StructuralIdentity,
            requiresReadback,
            inputDemand.StructuralIdentity,
            resources.Select(static binding => binding.Slot.ValueType).ToArray());
    }

    /// <summary>Gets the pure mapping from complete input bounds to conservative complete output bounds.</summary>
    public RenderBoundsContract Bounds { get; }

    /// <summary>Gets the mapping from this operation's output demand to the demand it places on its input.</summary>
    /// <remarks>
    /// A geometry operation that enlarges what it draws has to declare it, or the source it draws is
    /// rasterized at the density the consumer asked for and then stretched.
    /// </remarks>
    public RenderInputDemandContract InputDemand { get; }

    /// <summary>Gets the CPU-only hit-test contract for the conservative produced geometry.</summary>
    public RenderHitTestContract HitTest { get; }

    internal object DefinitionFingerprint { get; }

    /// <summary>Gets whether the callback is permitted to request declared input readback.</summary>
    public bool RequiresReadback { get; }

    /// <summary>Gets the non-null immutable list of non-null resources declared for the deferred callback.</summary>
    /// <remarks>
    /// Every resource must belong to the active request family when this description is recorded through
    /// <see cref="RenderNodeContext.Geometry(RenderFragmentHandle, GeometryDescription)"/>.
    /// </remarks>
    public IReadOnlyList<RenderResourceBinding> Resources { get; }

    internal void Render(GeometrySession session) => _execution.Invoke(session);

    internal object StructuralIdentity { get; }

    /// <summary>Creates a deferred geometry description.</summary>
    /// <param name="state">
    /// Immutable pixel-affecting state retained for execution.
    /// </param>
    /// <param name="render">
    /// A static execution callback. Its borrowed session and facades must not be retained.
    /// </param>
    /// <param name="bounds">An initialized pure input-to-output bounds contract.</param>
    /// <param name="hitTest">An initialized pure CPU output hit-test contract.</param>
    /// <param name="requiresReadback">Whether the callback may request declared readback of its input.</param>
    /// <param name="inputDemand">
    /// Maps resolved output demand to the density required from the input.
    /// </param>
    /// <param name="resources">
    /// Declared resources copied immediately, or <see langword="null"/>.
    /// </param>
    /// <param name="slots">
    /// Declared slots. <paramref name="resources"/> must bind each exactly once.
    /// </param>
    /// <returns>An immutable deferred geometry description.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="render"/> or <paramref name="state"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A contract, callback, resource, or slot binding is invalid.
    /// </exception>
    public static GeometryDescription Create<TState>(
        TState state,
        Action<GeometrySession, TState> render,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        bool requiresReadback = false,
        RenderInputDemandContract inputDemand = default,
        IEnumerable<RenderResourceBinding>? resources = null,
        IEnumerable<RenderResourceSlot>? slots = null)
        where TState : notnull
        => CreateCore(
            RenderDescriptionValidation.CreateStateChannel(
                state,
                render,
                nameof(state),
                nameof(render)),
            bounds,
            hitTest,
            RenderDescriptionValidation.StructuralIdentityOfExecution(render),
            requiresReadback,
            inputDemand,
            RenderDescriptionValidation.BindDeclaredSlots(
                slots,
                resources,
                nameof(slots),
                nameof(resources)));

    /// <summary>
    /// Creates a geometry description whose value can never satisfy a later request's cache lookup.
    /// </summary>
    /// <remarks>
    /// The opt-out for a callback whose pixel-affecting state cannot be expressed as a lightweight immutable
    /// key. The callback may capture, and the recorded value takes a fresh request-local identity every time.
    /// </remarks>
    internal static GeometryDescription CreateRequestLocal(
        Action<GeometrySession> render,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        bool requiresReadback = false,
        RenderInputDemandContract inputDemand = default,
        IEnumerable<RenderResourceBinding>? resources = null)
        => CreateCore(
            RenderDescriptionValidation.CreateRequestLocalChannel(render, nameof(render)),
            bounds,
            hitTest,
            render,
            requiresReadback,
            inputDemand,
            resources);

    internal static GeometryDescription CreateCore(
        RenderExecutionChannel<GeometrySession> execution,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        object definitionFingerprint,
        bool requiresReadback,
        RenderInputDemandContract inputDemand,
        IEnumerable<RenderResourceBinding>? resources)
    {
        bounds.ThrowIfUninitialized(nameof(bounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        ArgumentNullException.ThrowIfNull(definitionFingerprint);
        IReadOnlyList<RenderResourceBinding> resourceCopy = RenderDescriptionValidation.CopyResourceBindings(
            resources,
            nameof(resources));

        return new GeometryDescription(
            execution,
            bounds,
            hitTest,
            definitionFingerprint,
            requiresReadback,
            inputDemand,
            resourceCopy);
    }
}
