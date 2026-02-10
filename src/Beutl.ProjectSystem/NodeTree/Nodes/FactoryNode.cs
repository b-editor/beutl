using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes;

public class FactoryNode<T> : Node
    where T : EngineObject, new()
{
    public static readonly CoreProperty<T> ObjectProperty;

    public FactoryNode()
    {
        Object = new T();
        OutputSocket = AddOutput<T>("Output");
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

    protected OutputSocket<T> OutputSocket { get; }

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
