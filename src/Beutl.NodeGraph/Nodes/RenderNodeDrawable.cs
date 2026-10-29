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
        bool versionBumped = false;
        var resource = new Resource();
        resource.Reconcile(this, context, ref versionBumped);
        return resource;
    }

    public new sealed class Resource : Drawable.Resource
    {
        public RenderNode? GraphNode { get; set; }

        protected override Size MeasureCore(Size availableSize)
        {
            return Size.Empty;
        }

        protected override void OnDraw(GraphicsContext2D context)
        {
        }

        protected override void RenderCore(GraphicsContext2D context)
        {
            if (GraphNode != null)
            {
                context.DrawNode(
                    GraphNode,
                    n => new ReferencesChildRenderNode(n),
                    (refNode, n) => refNode.Update(n));
            }
        }

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            base.Reconcile(obj, context, ref versionBumped);
            if (obj is RenderNodeDrawable renderNode)
            {
                if (GraphNode != renderNode.GraphNode)
                {
                    GraphNode = renderNode.GraphNode;
                    Version++;
                    versionBumped = true;
                }

                if (GraphNode?.HasChanges == true)
                {
                    Version++;
                    versionBumped = true;
                }
            }

            CompleteReconciliation(context);
        }
    }
}
