using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.NodeTree.Nodes.Effects;

public abstract class FilterEffectNode<T> : ConfigureNode
    where T : FilterEffect, new()
{
    public FilterEffectNode()
    {
        Object = new T();
    }

    public T Object { get; set; }

    protected override void EvaluateCore()
    {
        FilterEffect.Resource? resource;

        if (OutputSocket.Value == null)
        {
            resource = Object.ToResource(RenderContext.Default);
            OutputSocket.Value = new FilterEffectRenderNode(resource);
        }
        else if (OutputSocket.Value is FilterEffectRenderNode { FilterEffect.Resource: { } filterEffect } node)
        {
            resource = filterEffect;
            bool updateOnly = false;
            resource.Update(Object, RenderContext.Default, ref updateOnly);
            node.Update(resource);
        }
    }
}
