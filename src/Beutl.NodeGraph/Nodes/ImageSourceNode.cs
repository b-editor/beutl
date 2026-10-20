using Beutl.Graphics.Rendering;
using Beutl.Media.Source;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes;

public sealed partial class ImageSourceNode : GraphNode
{
    public ImageSourceNode()
    {
        Output = AddOutput<ImageSourceRenderNode?>("Output", NodePortDisplays.Output);
        Source = AddInput<ImageSource?>("Source", NodePortDisplays.Source);
    }

    public OutputPort<ImageSourceRenderNode?> Output { get; }

    public InputPort<ImageSource?> Source { get; }

    public partial class Resource
    {
        private readonly ImageSourceOutput _output = new();

        public override void Update(GraphCompositionContext context)
        {
            Output = _output.Update(Source, context);
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                _output.Dispose();
            }
        }
    }
}
