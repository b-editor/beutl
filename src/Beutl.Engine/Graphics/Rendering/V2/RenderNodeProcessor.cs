using System.Runtime.CompilerServices;
using Beutl.Collections.Pooled;
using Beutl.Graphics.Rendering.V2.Cache;

namespace Beutl.Graphics.Rendering.V2;

public class RenderNodeProcessor
{
    private readonly IImmediateCanvasFactory _canvasFactory;
    private readonly ConditionalWeakTable<RenderNode, RenderNodeCache> _table = [];

    public RenderNodeProcessor(RenderNode root, IImmediateCanvasFactory canvasFactory)
    {
        _canvasFactory = canvasFactory;
        Root = root;
    }

    public RenderNode Root { get; set; }

    public RenderNodeCache GetCache(RenderNode node)
    {
        return _table.GetValue(node, key => new RenderNodeCache(key));
    }

    private void IncrementRenderCount(RenderNode node)
    {
        var cache = GetCache(node);
        if (node is ContainerRenderNode)
        {
            cache.CaptureChildren();
        }

        cache.IncrementRenderCount();
    }

    public RenderNodeOperation[] PullToRoot()
    {
        return Pull(Root);
    }

    public RenderNodeOperation[] Pull(RenderNode node)
    {
        if (node is ContainerRenderNode container)
        {
            using var operations = new PooledList<RenderNodeOperation>();
            foreach (RenderNode innerNode in container.Children)
            {
                operations.AddRange(Pull(innerNode));
            }

            var input = operations.ToArray();
            var context = new RenderNodeContext(_canvasFactory, input);
            var result = node.Process(context);
            IncrementRenderCount(node);
            return result;
        }
        else
        {
            var context = new RenderNodeContext(_canvasFactory, []);
            var result = node.Process(context);
            IncrementRenderCount(node);
            return result;
        }
    }
}
