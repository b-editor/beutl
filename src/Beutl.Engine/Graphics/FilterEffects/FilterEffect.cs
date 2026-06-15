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
        /// Builds the render node for this effect (feature 003). The default is supply-driven: run at the
        /// input supply density, capped by the global memory ceiling. An effect needing a different working
        /// scale (clamp-to-output for perf, oversample for SSAA) returns a <see cref="FilterEffectRenderNode"/>
        /// subclass that overrides <c>Process</c> and computes the scale itself; there is intentionally no
        /// per-effect <c>ResolutionPolicy</c> knob, since a custom render node is strictly more flexible.
        /// <para>
        /// Ergonomic caveat: there is no narrow "just change <c>w</c>" hook. <see cref="FilterEffectRenderNode.Process"/>
        /// computes the supply-driven scale inline and <c>base.Process(context)</c> ignores a subclass's <c>w</c>,
        /// so a custom working scale currently means reproducing the whole <c>Process</c> body (building the
        /// <see cref="FilterEffectContext"/>/<c>FilterEffectActivator</c> with your <c>w</c>). That copy can drift
        /// from the engine and silently miss later hardening — notably the FR-037(b) buffer-budget clamp
        /// (<see cref="RenderNodeContext.ClampWorkingScaleToBufferBudget"/>). A follow-up will shrink that copy
        /// surface; until then prefer the supply-driven default. See <c>contracts/effect-scale-contract.md</c>
        /// for the worked example.
        /// </para>
        /// </summary>
        public virtual FilterEffectRenderNode CreateRenderNode()
        {
            return new FilterEffectRenderNode(this);
        }

        public virtual PushedState Push(GraphicsContext2D context)
        {
            // feature 003: route through CreateRenderNode() so a custom FilterEffectRenderNode subclass
            // (the replacement for the removed ResolutionPolicy) is honoured on the Drawable push path too,
            // not only on the node-graph path.
            return context.PushNode(
                this,
                resource => resource.CreateRenderNode(),
                (node, resource) => node.Update(resource));
        }
    }
}
