namespace Beutl.Graphics.Effects;

/// <summary>
/// An imperative canvas-drawing node (feature 004, data-model §1, contract A2, research D7/D8): the honest
/// representation of composite geometry work (stroke, flat shadow, clip, layer) that cannot be a shader.
/// It is the sole descriptor kind that carries a rendering callback — a <see cref="Action{GeometrySession}"/>
/// the executor invokes with a bracketed canvas over a pooled output target. Never fused, always its own pass.
/// The <see cref="Bounds"/> contract is <b>mandatory</b> (there is no sensible default for coordinate-changing
/// geometry); the <see cref="StructuralToken"/> identifies the geometry kind for the structural key without
/// pinning the animated parameters the callback closes over (A4).
/// </summary>
public sealed record GeometryNodeDescriptor : EffectNodeDescriptor
{
    internal override EffectNodeKind Kind => EffectNodeKind.Geometry;

    private GeometryNodeDescriptor(
        Action<GeometrySession> render, BoundsContract bounds, object structuralToken, bool requiresReadback)
    {
        Render = render;
        Bounds = bounds;
        StructuralToken = structuralToken;
        RequiresReadback = requiresReadback;
    }

    /// <summary>The rendering callback the executor invokes with a session over the pass's output buffer.</summary>
    public Action<GeometrySession> Render { get; }

    /// <inheritdoc/>
    public override BoundsContract Bounds { get; }

    /// <inheritdoc/>
    public override bool IsCoordinateInvariant => false;

    /// <summary>Identity of the geometry <em>kind</em> for the structural key. Tokens share a plan only when their
    /// runtime types and <see cref="object.Equals(object?)"/> values match; equality and hash code must stay stable.</summary>
    public object StructuralToken { get; }

    /// <summary>
    /// True when the callback calls <see cref="EffectInput.Snapshot"/>. The executor performs and counts the
    /// required synchronization at the pass boundary before invoking the callback.
    /// </summary>
    public bool RequiresReadback { get; }

    /// <summary>
    /// Builds a geometry node from a render callback and its mandatory bounds contract. Both bounds functions must
    /// be non-null (an author who cannot lay out until execution passes <see cref="BoundsContract.RenderTime"/>).
    /// <paramref name="structuralToken"/> defaults to the callback's method identity, so callbacks built at the
    /// same call site (differing only in parameters) share a structural identity and never force a recompile.
    /// A geometry node consumes exactly one upstream operation — the executor materializes it as the single entry
    /// of <see cref="GeometrySession.Inputs"/>; fan-in belongs to <see cref="CompositeNodeDescriptor"/>.
    /// </summary>
    public static GeometryNodeDescriptor Create(
        Action<GeometrySession> render, BoundsContract bounds, object? structuralToken = null,
        bool requiresReadback = false)
    {
        ArgumentNullException.ThrowIfNull(render);
        bounds.ThrowIfUninitialized(nameof(bounds));
        return new GeometryNodeDescriptor(
            render, bounds, structuralToken ?? render.Method.MethodHandle.Value, requiresReadback);
    }
}
