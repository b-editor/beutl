using Beutl.Engine;
using Beutl.NodeGraph.Composition;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes;

public partial class FactoryNode<T> : GraphNode
    where T : EngineObject, new()
{
    public static readonly CoreProperty<T> ObjectProperty;

    public FactoryNode()
    {
        Object = new T();
        OutputPort = AddOutput<T>("Output");
        foreach (IProperty property in Object.Properties)
        {
            AddInput(Object, property);
        }
    }

    static FactoryNode()
    {
        ObjectProperty = ConfigureProperty<T, FactoryNode<T>>(nameof(Object))
            .Accessor(o => o.Object, (o, v) => o.Object = v)
            .Register();

        Hierarchy<FactoryNode<T>>(ObjectProperty);
    }

    [NotAutoSerialized]
    public T Object
    {
        get;
        set => SetAndRaise(ObjectProperty, ref field, value);
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
            var node = GetOriginal();
            OutputPort = node.Object;
        }
    }
}
