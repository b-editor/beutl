using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

public sealed partial class FallbackFilterEffect : FilterEffect, IFallback
{
    // An unresolved effect type renders as a passthrough. The generated ApplyTo throws (no legacy behavior to
    // reproduce); Describe appends no node so the graph is the identity — the input flows through unchanged.
    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
    }
}

[FallbackType(typeof(FallbackFilterEffect))]
[PresenterType(typeof(FilterEffectPresenter))]
public abstract partial class FilterEffect : EngineObject
{
    public abstract void ApplyTo(FilterEffectContext context, Resource resource);

    /// <summary>
    /// Describes this effect as a graph of node descriptors (feature 004, data-model §1, contract A1). Invoked by
    /// the engine whenever the effect's graph may be needed; it MUST be side-effect-free apart from appending
    /// descriptors — no rendering, no target allocation, no GPU calls — and read every animated value from
    /// <paramref name="resource"/>, never from live properties.
    /// </summary>
    /// <remarks>
    /// The default implementation is the rollout-step-3 transition bridge: it records the effect's legacy
    /// <see cref="ApplyTo"/> item list into a <see cref="FilterEffectContext"/> and appends a single
    /// opaque-legacy node wrapping it, so an unmigrated effect renders byte-identically through the retained
    /// activator machinery. Migrated effects override this to append typed descriptors (which fuse and cache);
    /// the bridge is deleted with <see cref="ApplyTo"/> in the final step.
    /// </remarks>
    public virtual void Describe(EffectGraphBuilder builder, Resource resource)
    {
        var context = new FilterEffectContext(builder.Bounds, builder.OutputScale, builder.WorkingScale);
        ApplyTo(context, resource);
        // The concrete effect type is the bridged node's structural identity: stable across a parameter animation
        // (so a chain containing this effect keeps hitting the plan cache) and distinct across effect kinds.
        builder.AppendOpaqueLegacy(context, resource.GetOriginal().GetType());
    }

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
