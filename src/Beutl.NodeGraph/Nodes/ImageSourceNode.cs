using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes;

public sealed partial class ImageSourceNode : GraphNode
{
    public ImageSourceNode()
    {
        Output = AddOutput<ImageSourceRenderNode>("Output");
        Source = AddInput<ImageSource?>("Source");
    }

    public OutputPort<ImageSourceRenderNode> Output { get; }

    public InputPort<ImageSource?> Source { get; }

    public partial class Resource
    {
        private ImageSourceRenderNode? _cachedOutput;
        private ImageSource.Resource? _sourceResource;
        private ImageSource? _lastSource;

        public override void Update(GraphCompositionContext context)
        {
            var source = Source;

            if (source == null)
            {
                if (_cachedOutput != null)
                {
                    _cachedOutput.Dispose();
                    _cachedOutput = null;
                }

                _sourceResource?.Dispose();
                _sourceResource = null;
                _lastSource = null;
                Output = null!;
                return;
            }

            if (_lastSource != source)
            {
                _sourceResource?.Dispose();
                _sourceResource = source.ToResource(context);
                _lastSource = source;
            }
            else
            {
                bool updateOnly = false;
                _sourceResource!.Update(source, context, ref updateOnly);
            }

            if (_cachedOutput == null)
            {
                _cachedOutput = new ImageSourceRenderNode(_sourceResource, Brushes.Resource.White, null);
            }
            else
            {
                _cachedOutput.Update(_sourceResource, Brushes.Resource.White, null);
            }

            Output = _cachedOutput;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                _sourceResource?.Dispose();
                _sourceResource = null;
                _cachedOutput?.Dispose();
                _cachedOutput = null;
            }
        }
    }
}
