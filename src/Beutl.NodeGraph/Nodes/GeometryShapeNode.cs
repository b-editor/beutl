using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes;

public sealed partial class GeometryShapeNode : GraphNode
{
    public GeometryShapeNode()
    {
        Output = AddOutput<GeometryRenderNode?>("Output");
        Geometry = AddInput<Geometry?>("Geometry");
        Fill = AddInput<Brush?>("Fill");
        Pen = AddInput<Pen?>("Pen");
    }

    public OutputPort<GeometryRenderNode?> Output { get; }

    public InputPort<Geometry?> Geometry { get; }

    public InputPort<Brush?> Fill { get; }

    public InputPort<Pen?> Pen { get; }

    public partial class Resource
    {
        private GeometryRenderNode? _cachedOutput;

        public override void Update(GraphCompositionContext context)
        {
            var geometry = Geometry;

            if (geometry == null)
            {
                if (_cachedOutput != null)
                {
                    _cachedOutput.Fill?.Resource.Dispose();
                    _cachedOutput.Pen?.Resource.Dispose();
                    _cachedOutput.Geometry?.Resource.Dispose();
                    _cachedOutput.Dispose();
                    _cachedOutput = null;
                }

                Output = null;
                return;
            }

            var fill = Fill;
            var pen = Pen;

            if (_cachedOutput == null)
            {
                var geometryResource = geometry.ToResource(context);
                var fillResource = fill?.ToResource(context);
                var penResource = pen?.ToResource(context);
                _cachedOutput = new GeometryRenderNode(geometryResource, fillResource, penResource);
            }
            else
            {
                var geometryResource = _cachedOutput.Geometry?.Resource;
                var fillResource = _cachedOutput.Fill?.Resource;
                var penResource = _cachedOutput.Pen?.Resource;

                if (geometryResource?.GetOriginal() != geometry)
                {
                    geometryResource?.Dispose();
                    geometryResource = geometry.ToResource(context);
                }
                else
                {
                    bool updateOnly = false;
                    geometryResource.Update(geometry, context, ref updateOnly);
                }

                if (fill == null || fillResource?.GetOriginal() != fill)
                {
                    fillResource?.Dispose();
                    fillResource = fill?.ToResource(context);
                }
                else
                {
                    bool updateOnly = false;
                    fillResource.Update(fill, context, ref updateOnly);
                }

                if (pen == null || penResource?.GetOriginal() != pen)
                {
                    penResource?.Dispose();
                    penResource = pen?.ToResource(context);
                }
                else
                {
                    bool updateOnly = false;
                    penResource.Update(pen, context, ref updateOnly);
                }

                _cachedOutput.Update(geometryResource, fillResource, penResource);
            }

            Output = _cachedOutput;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing && _cachedOutput != null)
            {
                _cachedOutput.Fill?.Resource.Dispose();
                _cachedOutput.Pen?.Resource.Dispose();
                _cachedOutput.Geometry?.Resource.Dispose();
                _cachedOutput.Dispose();
                _cachedOutput = null;
            }
        }
    }
}
