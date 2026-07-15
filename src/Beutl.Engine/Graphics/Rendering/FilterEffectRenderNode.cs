using Beutl.Engine;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Shared base for a <see cref="FilterEffect"/>'s render node: holds the captured <see cref="FilterEffect.Resource"/>
/// and the <see cref="Update"/>/<see cref="RenderNode.HasChanges"/> resource-diff plumbing that every subclass reuses.
/// It carries no plan-execution pipeline of its own — the default node produced by
/// <see cref="FilterEffect.Resource.PlanRenderNodeFactory"/> supplies that, along with the per-node plan and prefix
/// caches. A fully opaque effect instead derives from <see cref="CustomRenderNodeFilterEffect"/> and supplies a
/// <see cref="FilterEffectRenderNodeFactory"/> whose node implements <see cref="Process"/> itself.
/// </summary>
public abstract class FilterEffectRenderNode(FilterEffect.Resource filterEffect) : ContainerRenderNode
{
    internal FilterEffectRenderNodeFactory? CreationFactory { get; set; }

    public (FilterEffect.Resource Resource, int Version)? FilterEffect { get; private set; } = filterEffect.Capture();

    public bool Update(FilterEffect.Resource? fe)
    {
        if (!fe.Compare(FilterEffect))
        {
            FilterEffect = fe.Capture();
            HasChanges = true;
            return true;
        }

        return false;
    }

    public abstract override RenderNodeOperation[] Process(RenderNodeContext context);
}
