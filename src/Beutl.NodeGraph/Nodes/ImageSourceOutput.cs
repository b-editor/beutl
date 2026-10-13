using Beutl.Composition;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.NodeGraph.Nodes;

// The render node a node outputs for an image source, kept across frames: the source's resource is rebuilt
// when the source changes and reconciled otherwise.
internal sealed class ImageSourceOutput : IDisposable
{
    private ImageSourceRenderNode? _renderNode;
    private ImageSource.Resource? _sourceResource;
    private ImageSource? _lastSource;

    public ImageSourceRenderNode? Update(ImageSource? source, CompositionContext context)
    {
        if (source is null)
        {
            Dispose();
            return null;
        }

        if (!ReferenceEquals(_lastSource, source))
        {
            _sourceResource?.Dispose();
            _sourceResource = source.ToResource(context);
            _lastSource = source;
        }
        else
        {
            bool versionBumped = false;
            _sourceResource!.Reconcile(source, context, ref versionBumped);
        }

        if (_renderNode is null)
            _renderNode = new ImageSourceRenderNode(_sourceResource, Brushes.Resource.White, null);
        else
            _renderNode.Update(_sourceResource, Brushes.Resource.White, null);
        return _renderNode;
    }

    public void Dispose()
    {
        _renderNode?.Dispose();
        _renderNode = null;
        _sourceResource?.Dispose();
        _sourceResource = null;
        _lastSource = null;
    }
}
