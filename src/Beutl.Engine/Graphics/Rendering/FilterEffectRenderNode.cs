using Beutl.Engine;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Shared base for a <see cref="FilterEffect"/>'s render node: holds the captured <see cref="FilterEffect.Resource"/>
/// and the <see cref="Update"/>/<see cref="RenderNode.HasChanges"/> resource-diff plumbing that every subclass reuses.
/// It carries no plan-execution pipeline of its own — the default node returned by
/// <see cref="FilterEffect.Resource.CreateRenderNode"/> (an internal sealed subclass) supplies that, along with the
/// per-node plan and prefix caches. A plugin that overrides <see cref="FilterEffect.Resource.CreateRenderNode"/> to
/// return a custom subclass must implement <see cref="Process"/> itself and does not inherit that caching.
/// </summary>
public abstract class FilterEffectRenderNode(FilterEffect.Resource filterEffect) : ContainerRenderNode
{
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
