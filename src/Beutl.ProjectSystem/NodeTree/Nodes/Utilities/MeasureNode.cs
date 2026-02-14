using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities;

public partial class MeasureNode : Node
{
    public MeasureNode()
    {
        X = AddOutput<float>("X");
        Y = AddOutput<float>("Y");
        Width = AddOutput<float>("Width");
        Height = AddOutput<float>("Height");
        Input = AddInput<RenderNode>("Output");
    }

    public OutputSocket<float> X { get; }

    public OutputSocket<float> Y { get; }

    public OutputSocket<float> Width { get; }

    public OutputSocket<float> Height { get; }

    public InputSocket<RenderNode> Input { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            if (Input is RenderNode renderNode)
            {
                var processor = new RenderNodeProcessor(renderNode, true);
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
