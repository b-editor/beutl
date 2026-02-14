using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.NodeTree.Nodes.Utilities;

public class PreviewNode : Node
{
    private readonly InputSocket<RenderNode> _inputSocket;
    private readonly NodeMonitor<Ref<IBitmap>?> _preview;

    public PreviewNode()
    {
        _inputSocket = AddInput<RenderNode>("Input");
        _preview = AddImageMonitor("Preview");
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);

        if (!_preview.IsEnabled)
            return;

        if (_inputSocket.Value is RenderNode renderNode)
        {
            var processor = new RenderNodeProcessor(renderNode, true);
            var bitmap = processor.RasterizeAndConcat();
            _preview.Value?.Dispose();
            _preview.Value = Ref<IBitmap>.Create(bitmap);
        }
        else
        {
            _preview.Value?.Dispose();
            _preview.Value = null;
        }
    }
}
