using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes;

public sealed partial class GeometryShapeNode : GraphNode
{
    public GeometryShapeNode()
    {
        Output = AddOutput<GeometryRenderNode?>("Output", NodePortDisplays.Output);
        Geometry = AddInput<Geometry?>("Geometry", NodePortDisplays.Geometry);
        Fill = AddInput<Brush?>("Fill", NodePortDisplays.Fill);
        Pen = AddInput<Pen?>("Pen", NodePortDisplays.Pen);
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
                ReleaseCachedOutput();
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
                var geometryResource = Reuse(geometry, _cachedOutput.Geometry?.Resource, context)!;
                var fillResource = Reuse(fill, _cachedOutput.Fill?.Resource, context);
                var penResource = Reuse(pen, _cachedOutput.Pen?.Resource, context);
                _cachedOutput.Update(geometryResource, fillResource, penResource);
            }

            Output = _cachedOutput;

            // Rebuilds the resource when the input object changed or went away, and reconciles it otherwise.
            static TResource? Reuse<TObject, TResource>(TObject? value, TResource? current, GraphCompositionContext context)
                where TObject : EngineObject
                where TResource : EngineObject.Resource
            {
                if (value == null || current?.GetOriginal() != value)
                {
                    current?.Dispose();
                    return (TResource?)value?.ToResource(context);
                }

                bool versionBumped = false;
                current.Reconcile(value, context, ref versionBumped);
                return current;
            }
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseCachedOutput();
            }
        }

        // Disposes the cached render node together with the resources it holds.
        private void ReleaseCachedOutput()
        {
            if (_cachedOutput != null)
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
