using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Brushes;

public class FillNode : ConfigureNode
{
    private readonly InputSocket<IBrush> _brushSocket;

    public FillNode()
    {
        _brushSocket = AsInput<IBrush>("Brush");
    }

    protected override void Attach(Drawable drawable, object? state)
    {
    }

    protected override void Detach(Drawable drawable, object? state)
    {
        drawable.Fill = null;
    }

    protected override void EvaluateCore(Drawable drawable, object? state)
    {
        drawable.Fill = _brushSocket.Value;
    }
}
