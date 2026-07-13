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
        private static long s_nextStructuralId;
        private long _structuralId;

        /// <summary>
        /// A collision-free, process-stable token for this resource instance, lazily assigned on first read and
        /// constant thereafter. Used by <see cref="Rendering.StructuralKey"/> to give a custom-render-node node a
        /// reference identity that never aliases two distinct instances (unlike an object hash code, which can collide).
        /// </summary>
        internal long StructuralId
        {
            get
            {
                if (_structuralId == 0)
                    _structuralId = System.Threading.Interlocked.Increment(ref s_nextStructuralId);
                return _structuralId;
            }
        }

        /// <summary>
        /// The factory that creates this effect's render node — the concrete node type and its constructor as one
        /// value. The default runs the internal compiled-plan node (<see cref="PlanFilterEffectRenderNode"/>) with
        /// per-node plan and prefix caching. Override to supply a custom <see cref="FilterEffectRenderNode"/>
        /// subclass (e.g. a different working scale); such a subclass implements
        /// <see cref="FilterEffectRenderNode.Process"/> itself and does not inherit that caching. Because the
        /// factory captures the node type alongside its constructor, the render-graph diff's reuse check can never
        /// drift from the node actually created.
        /// </summary>
        public virtual FilterEffectRenderNodeFactory RenderNodeFactory
            => FilterEffectRenderNodeFactory.Of(static r => new PlanFilterEffectRenderNode(r));

        public virtual PushedState Push(GraphicsContext2D context)
        {
            FilterEffectRenderNodeFactory factory = RenderNodeFactory;
            return context.PushNode(
                this,
                factory.NodeType,
                factory.Create,
                static (node, resource) => node.Update(resource));
        }
    }
}
