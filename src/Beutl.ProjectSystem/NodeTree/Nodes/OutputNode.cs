using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.NodeTree.Nodes;

public class OutputNode : Node
{
    private readonly RenderNodeDrawable _drawable = new();

    public OutputNode()
    {
        InputSocket = AddInput<RenderNode>("Output");
    }

    protected InputSocket<RenderNode> InputSocket { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        RenderNode? input = InputSocket.Value;
        _drawable.Node = input;
        context.AddRenderable(_drawable);
    }

    [SuppressResourceClassGeneration]
    private sealed class RenderNodeDrawable : Drawable
    {
        public RenderNode? Node { get; set; }

        public override Resource ToResource(RenderContext context)
        {
            bool updateOnly = false;
            var resource = new _Resource();
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        protected override Size MeasureCore(Size availableSize, Resource resource)
        {
            return Size.Empty;
        }

        protected override void OnDraw(GraphicsContext2D context, Resource resource)
        {
        }

        public override void Render(GraphicsContext2D context, Resource resource)
        {
            var r = resource as _Resource;
            if (r?.Node != null)
            {
                context.DrawNode(r.Node, n => n, (n, _) => n.HasChanges);
            }
        }

        public sealed class _Resource : Resource
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
}
