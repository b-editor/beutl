using System.ComponentModel;
using Beutl.Engine;
using Beutl.NodeGraph.Composition;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes;

public partial class FactoryNode<T> : GraphNode
    where T : EngineObject, new()
{
    public FactoryNode()
    {
        ScanProperties<FactoryNode<T>>();
        HideProperty(ObjectProperty);
        ObjectProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Object)));
        Object = new T();
        OutputPort = AddOutput<T>("Output", NodePortDisplays.Output);
        foreach (IProperty property in Object.Properties)
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

    protected OutputPort<T> OutputPort { get; }

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
