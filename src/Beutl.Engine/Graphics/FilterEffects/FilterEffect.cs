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
        /// is strictly more flexible in EXPRESSIVE power.)
        /// <para>
        /// <b>Ergonomic caveat (today):</b> there is no narrow "just change <c>w</c>" hook —
        /// <see cref="FilterEffectRenderNode.Process"/> computes the supply-driven working scale inline and
        /// <c>base.Process(context)</c> ignores any <c>w</c> a subclass computes, so to run at a custom working
        /// scale you must currently reproduce the whole <c>Process</c> body (build the
        /// <see cref="FilterEffectContext"/>/<c>FilterEffectActivator</c> with your <c>w</c>). That copy can drift
        /// from the engine and silently miss later hardening — notably the FR-037(b) buffer-budget clamp
        /// (<see cref="RenderNodeContext.ClampWorkingScaleToBufferBudget"/>). A follow-up improves
        /// <c>FilterEffectRenderNode</c>'s general customizability to shrink that copy surface; until then, prefer
        /// the supply-driven default unless you genuinely need a non-supply <c>w</c>. See
        /// <c>contracts/effect-scale-contract.md</c> for the worked example.
        /// </para>
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
