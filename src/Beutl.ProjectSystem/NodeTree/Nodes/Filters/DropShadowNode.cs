using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Effects;

public class DropShadowNode : FilterEffectNode<DropShadow>
{
    private readonly InputSocket<Point> _posSocket;
    private readonly InputSocket<Size> _sigmaSocket;
    private readonly InputSocket<Color> _colorSocket;
    private readonly InputSocket<bool> _shadowOnlySocket;

    public DropShadowNode()
    {
        _posSocket = AsInput<Point>("Position").AcceptNumber();
        _sigmaSocket = AsInput<Size>("Sigma").AcceptNumber();
        _colorSocket = AsInput<Color>("Color");
        _shadowOnlySocket = AsInput<bool>("ShadowOnly");
    }

    protected override void EvaluateCore()
    {
        Object.Position.CurrentValue = _posSocket.Value;
        Object.Sigma.CurrentValue = _sigmaSocket.Value;
        Object.Color.CurrentValue = _colorSocket.Value;
        Object.ShadowOnly.CurrentValue = _shadowOnlySocket.Value;
        base.EvaluateCore();
    }
}
