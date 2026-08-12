using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

/// <summary>Defines the fixed shape of a deferred geometry operation.</summary>
/// <typeparam name="TState">The per-recording state supplied by a <see cref="GeometryCall{TState}"/>.</typeparam>
public sealed class GeometryDefinition<TState>
    where TState : notnull
{
    private readonly Action<GeometrySession, TState> _render;
    private readonly RenderBoundsContract _bounds;
    private readonly RenderHitTestContract _hitTest;
    private readonly bool _requiresReadback;
    private readonly IReadOnlyList<RenderResourceSlot> _resourceSlots;

    private GeometryDefinition(
        Action<GeometrySession, TState> render,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        bool requiresReadback,
        IReadOnlyList<RenderResourceSlot> resourceSlots)
    {
        _render = render;
        _bounds = bounds;
        _hitTest = hitTest;
        _requiresReadback = requiresReadback;
        _resourceSlots = resourceSlots;
    }

    /// <summary>Creates an immutable deferred geometry definition.</summary>
    public static GeometryDefinition<TState> Create(
        Action<GeometrySession, TState> render,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        bool requiresReadback = false,
        IEnumerable<RenderResourceSlot>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(render);
        bounds.ThrowIfUninitialized(nameof(bounds));
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        return new GeometryDefinition<TState>(
            render,
            bounds,
            hitTest,
            requiresReadback,
            RenderDescriptionValidation.CopyResourceSlots(resources, nameof(resources)));
    }

    /// <summary>Binds this operation shape to the state and resources for one recording.</summary>
    public GeometryCall<TState> Call(
        TState state,
        IEnumerable<RenderResourceBinding>? bindings = null)
        => new(this, state, bindings);

    internal GeometryDescription CreateDescription(
        TState state,
        IEnumerable<RenderResourceBinding>? bindings)
        => GeometryDescription.CreateCore(
            RenderDescriptionValidation.CreateStateChannel(
                state,
                _render,
                nameof(state),
                nameof(_render)),
            _bounds,
            _hitTest,
            definitionFingerprint: _render.Method,
            requiresReadback: _requiresReadback,
            resources: RenderDescriptionValidation.ValidateResourceBindings(
                _resourceSlots,
                bindings,
                nameof(bindings)));
}

/// <summary>Binds one geometry definition to one recording's state and resource tokens.</summary>
public sealed class GeometryCall<TState>
    where TState : notnull
{
    internal GeometryCall(
        GeometryDefinition<TState> definition,
        TState state,
        IEnumerable<RenderResourceBinding>? bindings)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        State = state;
        Description = definition.CreateDescription(state, bindings);
    }

    /// <summary>Gets the immutable operation shape.</summary>
    public GeometryDefinition<TState> Definition { get; }

    /// <summary>Gets the state supplied for this recording.</summary>
    public TState State { get; }

    internal GeometryDescription Description { get; }
}
