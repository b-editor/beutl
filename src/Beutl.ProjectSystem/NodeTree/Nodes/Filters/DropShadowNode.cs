using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Effects;

public class DropShadowNode : FilterEffectNode
{
    private readonly InputSocket<Point> _posSocket;
    private readonly InputSocket<Size> _sigmaSocket;
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
        context.State = new FilterEffectNodeEvaluationState(new DropShadow());
    }

    protected override void EvaluateCore(FilterEffect? state)
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
