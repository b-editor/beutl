using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities;

public partial class PreviewNode : Node
{
    private readonly NodeMonitor<Ref<IBitmap>?> _preview;

    public PreviewNode()
    {
        Input = AddInput<RenderNode>("Input");
        _preview = AddImageMonitor("Preview");
    }

    public InputSocket<RenderNode> Input { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            var node = GetOriginal();
            if (!node._preview.IsEnabled)
                return;

            if (Input is RenderNode renderNode)
            {
                var processor = new RenderNodeProcessor(renderNode, true);
                var bitmap = processor.RasterizeAndConcat();
                node._preview.Value?.Dispose();
                node._preview.Value = Ref<IBitmap>.Create(bitmap);
            }
            else
            {
                node._preview.Value?.Dispose();
                node._preview.Value = null;
            }
        }
    }
}
