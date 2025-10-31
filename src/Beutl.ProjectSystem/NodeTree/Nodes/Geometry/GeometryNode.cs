using Beutl.Graphics.Rendering;

namespace Beutl.NodeTree.Nodes.Geometry;

public class GeometryNode<T, TResource> : Node
    where T : Media.Geometry, new()
{
    public GeometryNode()
    {
        Object = new T();
        OutputSocket = AsOutput<Media.Geometry.Resource>("Geometry");
    }

    public T Object { get; }

    public OutputSocket<Media.Geometry.Resource> OutputSocket { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        Media.Geometry.Resource? resource;

        if (OutputSocket.Value == null)
        {
            resource = Object.ToResource(RenderContext.Default);
            OutputSocket.Value = resource;
        }
        else if (OutputSocket.Value is { } existingResource)
        {
            resource = existingResource;
            bool updateOnly = false;
            resource.Update(Object, RenderContext.Default, ref updateOnly);
        }
    }
}
