using Beutl.Graphics;
using Beutl.Graphics.Shapes;

namespace Beutl.NodeTree.Nodes;

public class RectNode : Node
{
    private readonly Rectangle _rectangle;
    private readonly InputSocket<float> _widthSocket;
    private readonly InputSocket<float> _heightSocket;
    private readonly InputSocket<float> _strokeSocket;

    public RectNode()
    {
        _rectangle = new Rectangle();
        AsOutput("Rectangle", _rectangle);

        _widthSocket = AsInput<float, Rectangle>(Drawable.WidthProperty, 100);
        _heightSocket = AsInput<float, Rectangle>(Drawable.HeightProperty, 100);
        _strokeSocket = AsInput<float, Rectangle>(Rectangle.StrokeWidthProperty, 4000);
    }

    public override void Evaluate(EvaluationContext context)
    {
        while (_rectangle.BatchUpdate)
        {
            _rectangle.EndBatchUpdate();
        }

        _rectangle.BeginBatchUpdate();
        _rectangle.Width = _widthSocket.Value;
        _rectangle.Height = _heightSocket.Value;
        _rectangle.StrokeWidth = _strokeSocket.Value;
    }
}
