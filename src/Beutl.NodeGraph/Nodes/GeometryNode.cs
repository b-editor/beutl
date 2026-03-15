using Beutl.Engine;
using Beutl.Media;
using Beutl.NodeGraph.Composition;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes;

public sealed partial class EllipseGeometryNode : GeometryNode<EllipseGeometry>;

public sealed partial class RectGeometryNode : GeometryNode<RectGeometry>;

public sealed partial class RoundedRectGeometryNode : GeometryNode<RoundedRectGeometry>;

public partial class GeometryNode<T> : GraphNode
    where T : Geometry, new()
{
    public static readonly CoreProperty<T> ObjectProperty;

    public GeometryNode()
    {
        Object = new T();
        OutputPort = AddOutput<T>("Output");
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

    public OutputPort<T> OutputPort { get; }

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

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            var node = GetOriginal();
            OutputPort = node.Object;
        }
    }
}
