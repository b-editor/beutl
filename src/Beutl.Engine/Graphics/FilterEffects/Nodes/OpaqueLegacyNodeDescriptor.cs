namespace Beutl.Graphics.Effects;

/// <summary>
/// A transition-only node (feature 004, rollout step 3, research D10) wrapping an unmigrated effect's recorded
/// legacy item list. The default <see cref="FilterEffect.Describe"/> bridge records an effect's <c>ApplyTo</c>
/// output into a <see cref="FilterEffectContext"/> and appends exactly one of these, so every built-in still
/// renders through the retained (internal-only) activator machinery — byte-identically — before the spatial,
/// compute and geometry primitives migrate. It is deleted with the bridge in step 6, never shipped as a shim.
/// </summary>
internal sealed record OpaqueLegacyNodeDescriptor(FilterEffectContext Context) : EffectNodeDescriptor
{
    public override EffectNodeKind Kind => EffectNodeKind.OpaqueLegacy;

    // The legacy machinery resolves its own bounds/ROIs at execution; the compiler must not try to lay it out.
    public override BoundsContract Bounds => BoundsContract.RenderTime;

    public override bool IsCoordinateInvariant => false;
}
