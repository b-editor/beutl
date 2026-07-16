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
        var resource = new Resource();
        try
        {
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }
        catch
        {
            try
            {
                resource.Dispose();
            }
            catch
            {
                // Preserve the acquisition failure while reclaiming the partially initialized drawable resource.
            }

            throw;
        }
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
        RenderNode? graphNode = r?.GraphNode;
        if (graphNode != null)
        {
            context.DrawNode(
                graphNode,
                n => new ReferencesChildRenderNode(n),
                (refNode, n) => refNode.Update(n));
        }
    }

    public new sealed class Resource : Drawable.Resource
    {
        private RenderNode? _graphNode;

        public RenderNode? GraphNode => ReadGeneratedResourceState(ref _graphNode);

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            var typed = (RenderNodeDrawable)obj;
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation(typed);
            base.Update(obj, context, ref updateOnly);
            if (obj is RenderNodeDrawable renderNode)
            {
                if (_graphNode != renderNode.GraphNode)
                {
                    _graphNode = renderNode.GraphNode;
                    Version++;
                    updateOnly = true;
                }

                if (_graphNode?.HasChanges == true)
                {
                    Version++;
                    updateOnly = true;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            _graphNode = null;
            base.Dispose(disposing);
        }
    }
}
