using Beutl.Engine;
using Beutl.Media;
using Beutl.NodeTree.Rendering;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes.Geometry;

public sealed class EllipseGeometryNode : GeometryNode<EllipseGeometry>;

public sealed class RectGeometryNode : GeometryNode<RectGeometry>;

public sealed class RoundedRectGeometryNode : GeometryNode<RoundedRectGeometry>;

public partial class GeometryNode<T> : Node
    where T : Media.Geometry, new()
{
    public static readonly CoreProperty<T> ObjectProperty;

    public GeometryNode()
    {
        Object = new T();
        OutputSocket = AddOutput<T>("Output");
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
        public override void Update(NodeRenderContext context)
        {
            var node = GetOriginal();
            OutputSocket = node.Object;
        }
    }
}
