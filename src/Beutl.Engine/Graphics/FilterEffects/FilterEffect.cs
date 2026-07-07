using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

public sealed partial class FallbackFilterEffect : FilterEffect, IFallback
{
    // An unresolved effect type renders as a passthrough: Describe appends no node so the graph is the identity and
    // the input flows through unchanged.
    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
    }
}

[FallbackType(typeof(FallbackFilterEffect))]
[PresenterType(typeof(FilterEffectPresenter))]
public abstract partial class FilterEffect : EngineObject
{
    /// <summary>
    /// Describes this effect as a graph of node descriptors (feature 004, data-model §1, contract A1). Invoked by
    /// the engine whenever the effect's graph may be needed; it MUST be side-effect-free apart from appending
    /// descriptors — no rendering, no target allocation, no GPU calls — and read every animated value from
    /// <paramref name="resource"/>, never from live properties.
    /// </summary>
    public abstract void Describe(EffectGraphBuilder builder, Resource resource);

    public abstract partial class Resource
    {
        /// <summary>
        /// Creates the render node for this effect. Override to supply a custom
        /// <see cref="FilterEffectRenderNode"/> subclass with a different working scale.
        /// </summary>
        public virtual FilterEffectRenderNode CreateRenderNode()
        {
            return new FilterEffectRenderNode(this);
        }

        public virtual PushedState Push(GraphicsContext2D context)
        {
            return context.PushNode(
                this,
                resource => resource.CreateRenderNode(),
                (node, resource) => node.Update(resource));
        }
    }
}
