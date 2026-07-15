using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities;

public partial class MeasureNode : GraphNode
{
    public MeasureNode()
    {
        X = AddOutput<float>("X");
        Y = AddOutput<float>("Y");
        Width = AddOutput<float>("Width");
        Height = AddOutput<float>("Height");
        Input = AddInput<RenderNode>("Output");
    }

    public OutputPort<float> X { get; }

    public OutputPort<float> Y { get; }

    public OutputPort<float> Width { get; }

    public OutputPort<float> Height { get; }

    public InputPort<RenderNode> Input { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            if (Input is RenderNode renderNode)
            {
                // Scale 1 intentional: GraphCompositionContext carries no output scale; bounds are logical-res.
                var processor = new RenderNodeProcessor(
                    renderNode, true, RenderIntent.Preview, pullPurpose: RenderPullPurpose.Auxiliary);
                RenderNodeOperation[] list = processor.PullToRoot();
                Rect rect = Rect.Empty;

                foreach (RenderNodeOperation item in list)
                {
                    rect = rect.Union(item.Bounds);
                    item.Dispose();
                }

                X = rect.X;
                Y = rect.Y;
                Width = rect.Width;
                Height = rect.Height;
            }
        }
    }
}
