using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

public sealed partial class FallbackFilterEffect : FilterEffect, IFallback;

[FallbackType(typeof(FallbackFilterEffect))]
[PresenterType(typeof(FilterEffectPresenter))]
public abstract partial class FilterEffect : EngineObject
{
    public abstract void ApplyTo(FilterEffectContext context, Resource resource);

    public abstract partial class Resource
    {
        /// <summary>
        /// Builds the render node for this effect (feature 003). The default is supply-driven
        /// (run at the input supply density, capped by the global memory ceiling). An effect that
        /// needs a different working scale — clamp-to-output for perf, oversample for SSAA — returns
        /// a <see cref="FilterEffectRenderNode"/> subclass that overrides <c>Process</c> and computes
        /// the working scale itself, rather than declaring a policy. (There is intentionally no
        /// per-effect <c>ResolutionPolicy</c> knob — no built-in needed one, and a custom render node
        /// is strictly more flexible.)
        /// </summary>
        public virtual FilterEffectRenderNode CreateRenderNode()
        {
            return new FilterEffectRenderNode(this);
        }

        public virtual PushedState Push(GraphicsContext2D context)
        {
            // feature 003: route through CreateRenderNode() so an effect that returns a custom
            // FilterEffectRenderNode subclass (the documented replacement for the removed ResolutionPolicy)
            // is actually honoured on the normal Drawable push path, not only on the node-graph path.
            return context.PushNode(
                this,
                resource => resource.CreateRenderNode(),
                (node, resource) => node.Update(resource));
        }
    }
}
