using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities;

public class MeasureNode : Node
{
    private readonly OutputSocket<float> _xSocket;
    private readonly OutputSocket<float> _ySocket;
    private readonly OutputSocket<float> _widthSocket;
    private readonly OutputSocket<float> _heightSocket;
    private readonly InputSocket<RenderNode> _inputSocket;

    public MeasureNode()
    {
        _xSocket = AsOutput<float>("X");
        _ySocket = AsOutput<float>("Y");
        _widthSocket = AsOutput<float>("Width");
        _heightSocket = AsOutput<float>("Height");
        _inputSocket = AsInput<RenderNode>("Drawable");
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        if (_inputSocket.Value is RenderNode renderNode)
        {
            var processor = new RenderNodeProcessor(renderNode, true);
            RenderNodeOperation[] list = processor.PullToRoot();
            Rect rect = Rect.Empty;

            foreach (RenderNodeOperation item in list)
            {
                rect = rect.Union(item.Bounds);
                item.Dispose();
            }

            _xSocket.Value = rect.X;
            _ySocket.Value = rect.Y;
            _widthSocket.Value = rect.Width;
            _heightSocket.Value = rect.Height;
        }
    }
}
