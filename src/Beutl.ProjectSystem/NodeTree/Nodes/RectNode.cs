using Beutl.Graphics;
using Beutl.Graphics.Shapes;

namespace Beutl.NodeTree.Nodes;

public class RectNode : Node
{
    private readonly Rectangle _rectangle;
    private readonly OutputSocket<Rectangle> _outputSocket;
    private readonly InputSocket<float> _widthSocket;
    private readonly InputSocket<float> _heightSocket;
    private readonly InputSocket<float> _strokeSocket;
    private readonly INodeItem[] _items;

    public RectNode()
    {
        _rectangle = new Rectangle();
        _outputSocket = new OutputSocket<Rectangle>()
        {
            Name = "Output",
            Value = _rectangle
        };
        _widthSocket = ToInput<float, Rectangle>(Drawable.WidthProperty, 100);
        _heightSocket = ToInput<float, Rectangle>(Drawable.HeightProperty, 100);
        _strokeSocket = ToInput<float, Rectangle>(Rectangle.StrokeWidthProperty, 4000);

        _items = new INodeItem[]
        {
            _outputSocket,
            _widthSocket,
            _heightSocket,
            _strokeSocket
        };
    }

    public override IReadOnlyList<INodeItem> Items => _items;

    public override void Evaluate(EvaluationContext context)
    {
        while (!_rectangle.EndBatchUpdate())
        {
        }

        _rectangle.BeginBatchUpdate();
        _rectangle.Width = _widthSocket.Value;
        _rectangle.Height = _heightSocket.Value;
        _rectangle.StrokeWidth = _strokeSocket.Value;

        base.Evaluate(context);
    }
}

public class LayerOutputNode : Node
{
    private readonly _Socket _renderableSocket;
    private readonly INodeItem[] _items;

    public LayerOutputNode()
    {
        _renderableSocket = new _Socket();
        _items = new INodeItem[]
        {
            _renderableSocket
        };
    }

    public override IReadOnlyList<INodeItem> Items => _items;

    public override void Evaluate(EvaluationContext context)
    {
        base.Evaluate(context);

        if (_renderableSocket.Value is { } value)
        {
            while (!value.EndBatchUpdate())
            {
            }
            context.AddRenderable(value);
        }
    }

    public sealed class _Socket : InputSocket<Drawable>
    {

    }
}
