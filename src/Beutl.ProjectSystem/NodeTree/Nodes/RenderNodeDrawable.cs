using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.NodeTree.Nodes;

[SuppressResourceClassGeneration]
public sealed class RenderNodeDrawable : Drawable
{
    public RenderNode? Node { get; set; }

    public override Resource ToResource(RenderContext context)
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
        if (r?.Node != null)
        {
            context.DrawNode(r.Node, n => n, (n, _) => n.HasChanges);
        }
    }

    public new sealed class Resource : Drawable.Resource
    {
        public RenderNode? Node { get; set; }

        public override void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            if (obj is RenderNodeDrawable renderNode)
            {
                if (Node != renderNode.Node)
                {
                    Node = renderNode.Node;
                    Version++;
                    updateOnly = true;
                }

                if (Node?.HasChanges == true)
                {
                    Version++;
                    updateOnly = true;
                }
            }
        }
    }
}
