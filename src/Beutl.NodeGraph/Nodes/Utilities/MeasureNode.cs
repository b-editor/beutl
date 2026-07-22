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
            Rect rect = Rect.Empty;
            if (Input is RenderNode renderNode)
            {
                if (FilterEffectInputBinding.TryGetCurrent(out FilterEffectInputBinding binding))
                {
                    binding.TryMeasureSubtree(renderNode, out rect);
                }
                else
                {
                    using var renderer = new RenderNodeRenderer(renderNode);
                    rect = renderer.Measure().QueryBounds;
                }
            }

            X = rect.X;
            Y = rect.Y;
            Width = rect.Width;
            Height = rect.Height;
        }
    }
}
