using System.Runtime.ExceptionServices;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes;

public sealed partial class ImageSourceNode : GraphNode
{
    public ImageSourceNode()
    {
        Output = AddOutput<ImageSourceRenderNode?>("Output");
        Source = AddInput<ImageSource?>("Source");
    }

    public OutputPort<ImageSourceRenderNode?> Output { get; }

    public InputPort<ImageSource?> Source { get; }

    public partial class Resource
    {
        private ImageSourceRenderNode? _cachedOutput;
        private ImageSource.Resource? _sourceResource;
        private ImageSource? _lastSource;

        protected override void UpdateCore(GraphCompositionContext context)
        {
            var source = Source;

            if (source == null)
            {
                ExceptionDispatchInfo? failure = ClearOwnedResource(ref _sourceResource);
                ImageSourceRenderNode? output = _cachedOutput;
                _cachedOutput = null;
                _lastSource = null;
                Output = null;
                try
                {
                    output?.Dispose();
                }
                catch (Exception ex)
                {
                    failure ??= ExceptionDispatchInfo.Capture(ex);
                }

                failure?.Throw();
                return;
            }

            ExceptionDispatchInfo? cleanupFailure = null;
            if (_lastSource != source)
            {
                ImageSource.Resource replacement = source.ToResource(context);
                cleanupFailure = ReplaceOwnedResource(ref _sourceResource, replacement);
                _lastSource = source;
            }
            else
            {
                bool updateOnly = false;
                _sourceResource!.Update(source, context, ref updateOnly);
            }

            ImageSource.Resource sourceResource = _sourceResource!;
            if (_cachedOutput == null)
            {
                _cachedOutput = new ImageSourceRenderNode(sourceResource, Brushes.Resource.White, null);
            }
            else
            {
                _cachedOutput.Update(sourceResource, Brushes.Resource.White, null);
            }

            Output = _cachedOutput;
            cleanupFailure?.Throw();
        }

        partial void PrepareResourceDispose(bool disposing, GeneratedResourceCleanupContext context)
        {
            if (disposing)
            {
                context.Reserve(_sourceResource);
            }
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            _sourceResource = null;
            ImageSourceRenderNode? cachedOutput = _cachedOutput;
            _cachedOutput = null;
            _lastSource = null;
            Output = null;

            Exception? failure = null;
            DisposeOwnedResources(ref failure, cachedOutput);
            ThrowIfCleanupFailed(failure);
        }
    }
}
