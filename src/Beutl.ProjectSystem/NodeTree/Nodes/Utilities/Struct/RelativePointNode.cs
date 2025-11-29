using Beutl.Graphics;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class RelativePointNode : Node
{
    private readonly OutputSocket<RelativePoint> _valueSocket;
    private readonly NodeItem<RelativeUnit> _unitSocket;
    private readonly InputSocket<float> _xSocket;
    private readonly InputSocket<float> _ySocket;

    public RelativePointNode()
    {
        _valueSocket = AsOutput("RelativePoint", RelativePoint.TopLeft);
        _unitSocket = AsProperty<RelativeUnit>("Unit");
        _xSocket = AsInput<float>("X").AcceptNumber();
        _ySocket = AsInput<float>("Y").AcceptNumber();
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new RelativePoint(_xSocket.Value, _ySocket.Value, _unitSocket.Value);
    }
}
