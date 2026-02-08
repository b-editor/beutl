using Beutl.Engine;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes.Brushes;

public class BrushNode<T> : Node
    where T : Brush, new()
{
    public static readonly CoreProperty<T> ObjectProperty;

    public BrushNode()
    {
        Object = new T();
        OutputSocket = AsOutput<T>("Output");
        foreach (IProperty property in Object.Properties)
        {
            AddInput(Object, property);
        }
    }

    static BrushNode()
    {
        ObjectProperty = ConfigureProperty<T, BrushNode<T>>(nameof(Object))
            .Accessor(o => o.Object, (o, v) => o.Object = v)
            .Register();

        Hierarchy<BrushNode<T>>(ObjectProperty);
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
