using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Base class for a filter effect whose execution lives in a custom <see cref="FilterEffectRenderNode"/> — supplied
/// by overriding <see cref="Resource.RenderNodeFactory"/> — rather than in describable descriptors (the
/// canonical case being <c>NodeGraphFilterEffect</c>, which evaluates a node graph). <see cref="Describe"/> is sealed
/// to append a single custom-render-node node for the effect's own resource, so the effect is describable — and
/// therefore group-safe — everywhere a container walks its children (a <see cref="FilterEffectGroup"/>, a
/// <see cref="DelayAnimationEffect"/> branch); a top-level instance still renders through its own render node.
/// <para>
/// Derive from this whenever you supply a custom render node. Its dedicated abstract <see cref="Resource"/> requires
/// the factory at compile time, while the sealed <see cref="Describe"/> makes the effect group-safe by construction.
/// </para>
/// </summary>
public abstract partial class CustomRenderNodeFilterEffect : FilterEffect
{
    /// <summary>
    /// Resource base for custom-render-node effects. A derived resource cannot accidentally inherit the default plan
    /// factory: it must provide the custom node factory that the sealed <see cref="Describe"/> embeds.
    /// </summary>
    public new abstract partial class Resource
    {
        /// <summary>
        /// A retained singleton factory for the fully opaque render node. Returning a new factory instance invalidates
        /// node reuse and is therefore unsupported.
        /// </summary>
        public abstract FilterEffectRenderNodeFactory RenderNodeFactory { get; }
    }

    /// <inheritdoc/>
    public sealed override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        if (resource is not Resource customResource)
        {
            throw new ArgumentException(
                $"A {nameof(CustomRenderNodeFilterEffect)} must use a resource derived from "
                + $"{typeof(Resource).FullName}.", nameof(resource));
        }

        builder.Effect(customResource);
    }
}
