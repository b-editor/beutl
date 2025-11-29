using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes;

public sealed class GeometryShapeNode : Node
{
    private readonly OutputSocket<GeometryRenderNode> _outputSocket;
    private readonly InputSocket<Media.Geometry.Resource?> _geometrySocket;
    private readonly InputSocket<Brush.Resource?> _fillSocket;
    private readonly InputSocket<Pen.Resource?> _penSocket;

    public GeometryShapeNode()
    {
        _outputSocket = AsOutput<GeometryRenderNode>("GeometryNode");
        _geometrySocket = AsInput<Media.Geometry.Resource?>("Geometry");
        _fillSocket = AsInput<Brush.Resource?>("Fill");
        _penSocket = AsInput<Pen.Resource?>("Pen");
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        if (_geometrySocket.Value == null)
        {
            _outputSocket.Value?.Dispose();
            _outputSocket.Value = null;
            return;
        }

        if (_outputSocket.Value == null)
        {
            _outputSocket.Value = new GeometryRenderNode(_geometrySocket.Value, _fillSocket.Value, _penSocket.Value);
        }
        else
        {
            _outputSocket.Value.Update(_geometrySocket.Value, _fillSocket.Value, _penSocket.Value);
        }
    }
}
