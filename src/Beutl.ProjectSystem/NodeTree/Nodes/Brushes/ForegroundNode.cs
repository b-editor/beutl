using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Brushes;

public class ForegroundNode : ConfigureNode
{
    private readonly InputSocket<IBrush> _brushSocket;

    public ForegroundNode()
    {
        _brushSocket = AsInput<IBrush>("Brush", "Brush");
    }

    protected override void Attach(Drawable drawable, object? state)
    {
    }

    protected override void Detach(Drawable drawable, object? state)
    {
        drawable.Foreground = null;
    }

    protected override void EvaluateCore(NodeEvaluationContext context)
    {
        if (InputSocket.Value is { } drawable)
        {
            drawable.Foreground = _brushSocket.Value;
        }
    }
}
