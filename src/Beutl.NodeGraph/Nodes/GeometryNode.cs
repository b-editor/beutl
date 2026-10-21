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
        Object = new T();
        OutputPort = AddOutput<T>("Output", NodePortDisplays.Output);
        // The object's base fields, such as IsEnabled, are not inputs.
        foreach (IProperty property in Object.Properties.Where(property => !Object.IsBaseField(property)))
        {
            AddInput(Object, property);
        }
    }

    [NotAutoSerialized]
    [SuppressResourceClassGeneration]
    public IProperty<T> ObjectProperty { get; } = new ValueOnlyProperty<T>(null!) { AffectsRender = false };

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
