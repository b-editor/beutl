using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities;

public partial class PreviewNode : GraphNode
{
    private readonly object _previewLock = new();
    private readonly NodeMonitor<Ref<Bitmap>?> _preview;

    public PreviewNode()
    {
        Input = AddInput<RenderNode>("Input");
        _preview = AddImageMonitor("Preview");
    }

    public InputPort<RenderNode> Input { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            var node = GetOriginal();
            if (!node._preview.IsEnabled)
                return;

            if (FilterEffectInputBinding.TryGetCurrent(out FilterEffectInputBinding binding))
            {
                binding.RegisterPreview(Input, node.SwapPreview);
            }
            else if (Input is RenderNode renderNode)
            {
                // A TargetDomain also widens the output extent, so it serves only as a fallback
                // owner for graphs whose Full target access cannot resolve without one.
                try
                {
                    using var renderer = new RenderNodeRenderer(renderNode);
                    using RenderNodeRasterization rasterization = renderer.Rasterize();
                    node.ReplacePreview(rasterization.Bitmap?.Clone());
                }
                catch (RenderTargetDomainRequiredException) when (context.TargetDomain is { } domain)
                {
                    using var renderer = new RenderNodeRenderer(renderNode, new RenderNodeRendererOptions
                    {
                        DefaultRequest = new RenderNodeRenderRequest
                        {
                            TargetDomain = domain,
                        },
                    });
                    using RenderNodeRasterization rasterization = renderer.Rasterize();
                    node.ReplacePreview(rasterization.Bitmap?.Clone());
                }
            }
            else
            {
                node.ReplacePreview(null);
            }
        }
    }

    private void ReplacePreview(Bitmap? bitmap)
    {
        Ref<Bitmap>? replacement = bitmap is null ? null : Ref<Bitmap>.Create(bitmap);
        Ref<Bitmap>? previous = null;
        try
        {
            previous = SwapPreview(replacement);
            replacement = null;
        }
        finally
        {
            replacement?.Dispose();
            previous?.Dispose();
        }
    }

    private Ref<Bitmap>? SwapPreview(Ref<Bitmap>? replacement)
    {
        lock (_previewLock)
        {
            Ref<Bitmap>? previous = _preview.Value;
            try
            {
                _preview.Value = replacement;
            }
            catch (Exception assignmentFailure)
            {
                try
                {
                    _preview.Value = previous;
                }
                catch (Exception restoreFailure)
                {
                    throw new AggregateException(
                        "The preview monitor rejected both the replacement and restoration notifications.",
                        assignmentFailure,
                        restoreFailure);
                }

                throw;
            }

            return previous;
        }
    }
}
