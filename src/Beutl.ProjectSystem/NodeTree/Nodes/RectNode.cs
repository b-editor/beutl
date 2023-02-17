using Beutl.Graphics;
using Beutl.Graphics.Shapes;

namespace Beutl.NodeTree.Nodes;

public class RectNode : Node
{
    private readonly OutputSocket<Rectangle> _outputSocket;
    private readonly InputSocket<float> _widthSocket;
    private readonly InputSocket<float> _heightSocket;
    private readonly InputSocket<float> _strokeSocket;

    public RectNode()
    {
        _outputSocket = AsOutput<Rectangle>("Rectangle");

        _widthSocket = AsInput<float, Rectangle>(Drawable.WidthProperty, value: 100).AcceptNumber();
        _heightSocket = AsInput<float, Rectangle>(Drawable.HeightProperty, value: 100).AcceptNumber();
        _strokeSocket = AsInput<float, Rectangle>(Rectangle.StrokeWidthProperty, value: 4000).AcceptNumber();
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new Rectangle();
    }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        context.State = null;
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        Rectangle rectangle = context.GetOrSetState<Rectangle>();
        while (rectangle.BatchUpdate)
        {
            rectangle.EndBatchUpdate();
        }

        rectangle.BeginBatchUpdate();
        rectangle.Width = _widthSocket.Value;
        rectangle.Height = _heightSocket.Value;
        rectangle.StrokeWidth = _strokeSocket.Value;

        _outputSocket.Value = rectangle;
    }
}
