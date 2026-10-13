using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

[FallbackType(typeof(FallbackFilterEffect))]
[PresenterType(typeof(FilterEffectPresenter))]
public abstract partial class FilterEffect : EngineObject
{
    public abstract void ApplyTo(FilterEffectContext context, Resource resource);

    public abstract partial class Resource
    {
        /// <summary>
        /// Creates the render node for this effect. Override to supply a custom
        /// <see cref="FilterEffectRenderNode"/> subclass with a different working scale.
        /// </summary>
        public virtual FilterEffectRenderNode CreateRenderNode()
        {
            if (GetOriginal() is null)
            {
                throw new InvalidOperationException(
                    "The default FilterEffectRenderNode cannot be created from a detached filter-effect resource. "
                    + "Override CreateRenderNode() to provide a render node that supports detached resources.");
            }

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
