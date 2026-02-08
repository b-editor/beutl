using Beutl.Engine;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes.Geometry;

public sealed class EllipseGeometryNode : GeometryNode<EllipseGeometry>;

public sealed class RectGeometryNode : GeometryNode<RectGeometry>;

public sealed class RoundedRectGeometryNode : GeometryNode<RoundedRectGeometry>;

public class GeometryNode<T> : Node
    where T : Media.Geometry, new()
{
    public static readonly CoreProperty<T> ObjectProperty;

    public GeometryNode()
    {
        Object = new T();
        OutputSocket = AsOutput<T>("Output");
        foreach (IProperty property in Object.Properties)
        {
            AddInput(Object, property);
        }
    }

    static GeometryNode()
    {
        ObjectProperty = ConfigureProperty<T, GeometryNode<T>>(nameof(Object))
            .Accessor(o => o.Object, (o, v) => o.Object = v)
            .Register();

        Hierarchy<GeometryNode<T>>(ObjectProperty);
    }

    [NotAutoSerialized]
    public T Object
    {
        get;
        set => SetAndRaise(ObjectProperty, ref field, value);
    }

    public OutputSocket<T> OutputSocket { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        OutputSocket.Value = Object;
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue("Object", Object);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        context.Populate("Object", Object);
    }
}
