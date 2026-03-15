using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.NodeGraph.Nodes;

[SuppressResourceClassGeneration]
public sealed class RenderNodeDrawable : Drawable
{
    public RenderNode? GraphNode { get; set; }

    public override Resource ToResource(CompositionContext context)
    {
        bool updateOnly = false;
        var resource = new Resource();
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        return Size.Empty;
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = resource as Resource;
        if (r?.GraphNode != null)
        {
            context.DrawNode(
                r.GraphNode,
                n => new ReferencesChildRenderNode(n),
                (refNode, n) => refNode.Update(n));
        }
    }

    public new sealed class Resource : Drawable.Resource
    {
        public RenderNode? GraphNode { get; set; }

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            if (obj is RenderNodeDrawable renderNode)
            {
                if (GraphNode != renderNode.GraphNode)
                {
                    GraphNode = renderNode.GraphNode;
                    Version++;
                    updateOnly = true;
                }

                if (GraphNode?.HasChanges == true)
                {
                    Version++;
                    updateOnly = true;
                }
            }
        }
    }
}
