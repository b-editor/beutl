using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Base class for a filter effect whose execution lives in a custom <see cref="FilterEffectRenderNode"/> — supplied
/// by overriding <see cref="FilterEffect.Resource.RenderNodeFactory"/> — rather than in describable descriptors (the
/// canonical case being <c>NodeGraphFilterEffect</c>, which evaluates a node graph). <see cref="Describe"/> is sealed
/// to append a single custom-render-node node for the effect's own resource, so the effect is describable — and
/// therefore group-safe — everywhere a container walks its children (a <see cref="FilterEffectGroup"/>, a
/// <see cref="DelayAnimationEffect"/> branch); a top-level instance still renders through its own render node.
/// <para>
/// Derive from this whenever you override <see cref="FilterEffect.Resource.RenderNodeFactory"/>: it makes the effect
/// group-safe by construction, so a subclass cannot fall into the trap of leaving <see cref="FilterEffect.Describe"/>
/// abstract (or throwing from it) and crashing every container that holds one.
/// </para>
/// </summary>
[SuppressResourceClassGeneration]
public abstract partial class CustomRenderNodeFilterEffect : FilterEffect
{
    /// <inheritdoc/>
    public sealed override void Describe(EffectGraphBuilder builder, Resource resource)
        => builder.CustomRenderNode(resource);
}
