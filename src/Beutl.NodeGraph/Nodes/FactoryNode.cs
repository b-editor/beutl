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
        HideProperty(Object);
        Object.Value = new T();
        OutputPort = AddOutput<T>("Output", NodePortDisplays.Output);
        // The object's base fields, such as IsEnabled, are not inputs.
        foreach (IProperty property in Object.Value.Properties.Where(property => !Object.Value.IsBaseField(property)))
        {
            AddInput(Object.Value, property);
        }
    }

    [NotAutoSerialized]
    [SuppressResourceClassGeneration]
    public IProperty<T> Object { get; } = new ValueOnlyProperty<T>(null!) { AffectsRender = false };

    protected OutputPort<T> OutputPort { get; }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue("Object", Object.Value);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        context.Populate("Object", Object.Value);
    }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            var node = RequireOriginal();
            OutputPort = node.Object.Value;
        }
    }
}
