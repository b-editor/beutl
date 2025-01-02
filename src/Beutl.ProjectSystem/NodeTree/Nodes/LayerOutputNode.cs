using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes;

public class LayerOutputNode : OutputNode
{
    private readonly InputSocket<BlendMode> _blendModeSocket;
    private readonly InputSocket<AlignmentX> _alignmentXSocket;
    private readonly InputSocket<AlignmentY> _alignmentYSocket;
    private readonly InputSocket<RelativePoint> _originSocket;

    public LayerOutputNode()
    {
        _blendModeSocket = AsInput(Drawable.BlendModeProperty);
        _alignmentXSocket = AsInput(Drawable.AlignmentXProperty);
        _alignmentYSocket = AsInput(Drawable.AlignmentYProperty);
        _originSocket = AsInput(Drawable.TransformOriginProperty);
    }

    protected override void Attach(Drawable drawable)
    {
    }

    protected override void Detach(Drawable drawable)
    {
        drawable.BlendMode = BlendMode.SrcOver;
        drawable.AlignmentX = AlignmentX.Center;
        drawable.AlignmentY = AlignmentY.Center;
        drawable.TransformOrigin = RelativePoint.Center;
    }

    protected override void EvaluateCore(NodeEvaluationContext context)
    {
        if (InputSocket is { Value: { } value, Connection: { } })
        {
            value.BlendMode = _blendModeSocket.Value;
            value.AlignmentX = _alignmentXSocket.Value;
            value.AlignmentY = _alignmentYSocket.Value;
            value.TransformOrigin = _originSocket.Value;

            context.AddRenderable(value);
        }
    }
}
