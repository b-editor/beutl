using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes;

public sealed partial class GeometryShapeNode : Node
{
    public GeometryShapeNode()
    {
        Output = AddOutput<GeometryRenderNode>("Output");
        Geometry = AddInput<Media.Geometry?>("Geometry");
        Fill = AddInput<Brush?>("Fill");
        Pen = AddInput<Pen?>("Pen");
    }

    public OutputSocket<GeometryRenderNode> Output { get; }

    public InputSocket<Media.Geometry?> Geometry { get; }

    public InputSocket<Brush?> Fill { get; }

    public InputSocket<Pen?> Pen { get; }

    public partial class Resource
    {
        private GeometryRenderNode? _cachedOutput;

        public override void Update(NodeRenderContext context)
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

                Output = null!;
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

                if (fillResource?.GetOriginal() != fill || fill == null)
                {
                    fillResource?.Dispose();
                    fillResource = fill?.ToResource(context);
                }
                else
                {
                    bool updateOnly = false;
                    fillResource.Update(fill, context, ref updateOnly);
                }

                if (penResource?.GetOriginal() != pen || pen == null)
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
