using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

public sealed partial class FallbackFilterEffect : FilterEffect, IFallback;

[FallbackType(typeof(FallbackFilterEffect))]
[PresenterType(typeof(FilterEffectPresenter))]
public abstract partial class FilterEffect : EngineObject
{
    /// <summary>
    /// How this effect's buffer-allocating boundary derives its working scale (feature 003).
    /// Defaults to <see cref="ResolutionPolicy.Inherit"/> (supply-driven); contour / morphology
    /// effects override to <see cref="ResolutionPolicy.PreserveSource"/>.
    /// </summary>
    public virtual ResolutionPolicy ResolutionPolicy => ResolutionPolicy.Inherit;

    public abstract void ApplyTo(FilterEffectContext context, Resource resource);

    public abstract partial class Resource
    {
        public virtual FilterEffectRenderNode CreateRenderNode()
        {
            return new FilterEffectRenderNode(this);
        }

        public virtual PushedState Push(GraphicsContext2D context)
        {
            return context.PushNode(
                this,
                resource => new FilterEffectRenderNode(resource),
                (node, resource) => node.Update(resource));
        }
    }
}
