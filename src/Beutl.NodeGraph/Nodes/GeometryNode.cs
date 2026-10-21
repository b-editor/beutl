using System.ComponentModel;
using Beutl.Engine;
using Beutl.Media;
using Beutl.NodeGraph.Composition;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes;

public partial class GeometryNode<T> : GraphNode
    where T : Geometry, new()
{
    public GeometryNode()
    {
        ScanProperties<GeometryNode<T>>();
        HideProperty(ObjectProperty);
        ObjectProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Object)));
        OutputPort = AddOutput<T>("Output", NodePortDisplays.Output);
        foreach (IProperty property in Object.Properties)
        {
            AddInput(Object, property);
        }
    }

    // The object is the property's first value, so the property does not pass on its edits, which the ports pass on.
    [NotAutoSerialized]
    [SuppressResourceClassGeneration]
    public IProperty<T> ObjectProperty { get; } = Property.CreateValue(new T());

    public T Object
    {
        get => ObjectProperty.Value;
        set => ObjectProperty.Value = value;
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
            var node = RequireOriginal();
            OutputPort = node.Object;
        }
    }
}
