using Beutl.Graphics;
using Beutl.Graphics.Filters;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Filters;

public class DropShadowNode : ImageFilterNode
{
    private readonly InputSocket<Point> _posSocket;
    private readonly InputSocket<Vector> _sigmaSocket;
    private readonly InputSocket<Color> _colorSocket;
    private readonly InputSocket<bool> _shadowOnlySocket;

    public DropShadowNode()
    {
        _posSocket = AsInput(DropShadow.PositionProperty).AcceptNumber();
        _sigmaSocket = AsInput(DropShadow.SigmaProperty).AcceptNumber();
        _colorSocket = AsInput(DropShadow.ColorProperty);
        _shadowOnlySocket = AsInput(DropShadow.ShadowOnlyProperty);
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new ImageFilterNodeEvaluationState(new DropShadow());
    }

    protected override void EvaluateCore(IImageFilter? state)
    {
        if (state is DropShadow model)
        {
            model.Position = _posSocket.Value;
            model.Sigma = _sigmaSocket.Value;
            model.Color = _colorSocket.Value;
            model.ShadowOnly = _shadowOnlySocket.Value;
        }
    }
}
