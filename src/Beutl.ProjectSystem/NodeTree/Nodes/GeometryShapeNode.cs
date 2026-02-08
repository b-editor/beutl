using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes;

public sealed class GeometryShapeNode : Node
{
    private readonly OutputSocket<GeometryRenderNode> _outputSocket;
    private readonly InputSocket<Media.Geometry?> _geometrySocket;
    private readonly InputSocket<Brush?> _fillSocket;
    private readonly InputSocket<Pen?> _penSocket;

    public GeometryShapeNode()
    {
        _outputSocket = AsOutput<GeometryRenderNode>("Output");
        _geometrySocket = AsInput<Media.Geometry?>("Geometry");
        _fillSocket = AsInput<Brush?>("Fill");
        _penSocket = AsInput<Pen?>("Pen");
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        if (_geometrySocket.Value == null)
        {
            _outputSocket.Value?.Fill?.Resource.Dispose();
            _outputSocket.Value?.Pen?.Resource.Dispose();
            _outputSocket.Value?.Geometry?.Resource.Dispose();
            _outputSocket.Value?.Dispose();
            _outputSocket.Value = null;
            return;
        }

        var renderContext = new RenderContext(context.Renderer.Time);
        if (_outputSocket.Value == null)
        {
            var geometryResource = _geometrySocket.Value.ToResource(renderContext);
            var fillResource = _fillSocket.Value?.ToResource(renderContext);
            var penResource = _penSocket.Value?.ToResource(renderContext);
            _outputSocket.Value = new GeometryRenderNode(geometryResource, fillResource, penResource);
        }
        else
        {
            var geometryResource = _outputSocket.Value.Geometry?.Resource;
            var fillResource = _outputSocket.Value.Fill?.Resource;
            var penResource = _outputSocket.Value.Pen?.Resource;

            if (geometryResource?.GetOriginal() != _geometrySocket.Value)
            {
                geometryResource?.Dispose();
                geometryResource = _geometrySocket.Value.ToResource(renderContext);
            }
            else
            {
                bool updateOnly = false;
                geometryResource.Update(_geometrySocket.Value, renderContext, ref updateOnly);
            }

            if (fillResource?.GetOriginal() != _fillSocket.Value || _fillSocket.Value == null)
            {
                fillResource?.Dispose();
                fillResource = _fillSocket.Value?.ToResource(renderContext);
            }
            else
            {
                bool updateOnly = false;
                fillResource?.Update(_fillSocket.Value, renderContext, ref updateOnly);
            }

            if (penResource?.GetOriginal() != _penSocket.Value || _penSocket.Value == null)
            {
                penResource?.Dispose();
                penResource = _penSocket.Value?.ToResource(renderContext);
            }
            else
            {
                bool updateOnly = false;
                penResource?.Update(_penSocket.Value, renderContext, ref updateOnly);
            }

            _outputSocket.Value.Update(geometryResource, fillResource, penResource);
        }
    }
}
