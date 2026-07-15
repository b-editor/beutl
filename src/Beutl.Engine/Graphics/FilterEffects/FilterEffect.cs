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
        private static readonly PlanFilterEffectRenderNodeFactory s_defaultPlanRenderNodeFactory =
            PlanFilterEffectRenderNodeFactory.Of<Resource, PlanFilterEffectRenderNode>(
                static resource => new PlanFilterEffectRenderNode(resource));
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
        /// Creates the standard compiled-plan render node. Override with a retained singleton factory whose node
        /// derives from <see cref="PlanFilterEffectRenderNode"/> to customize a narrow execution policy while keeping
        /// compiler, ROI, pooling, and cache behavior. Fully opaque execution belongs to
        /// <see cref="CustomRenderNodeFilterEffect.Resource"/> instead.
        /// </summary>
        public virtual PlanFilterEffectRenderNodeFactory PlanRenderNodeFactory
            => s_defaultPlanRenderNodeFactory;

        internal (FilterEffectRenderNodeFactory Factory, bool CanInline) ResolveRenderNodeFactory()
        {
            if (this is CustomRenderNodeFilterEffect.Resource custom)
            {
                FilterEffectRenderNodeFactory customFactory = custom.RenderNodeFactory
                    ?? throw new InvalidOperationException("A custom filter effect returned a null render-node factory.");
                return (customFactory, false);
            }

            PlanFilterEffectRenderNodeFactory planFactory = PlanRenderNodeFactory
                ?? throw new InvalidOperationException("A filter effect returned a null plan render-node factory.");
            return (planFactory.Inner, ReferenceEquals(planFactory, s_defaultPlanRenderNodeFactory));
        }

    }
}
