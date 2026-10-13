using Beutl.Serialization;

namespace Beutl.NodeGraph;

// Earlier builds also wrote the property name and the adapter type here. The port finds its property by Name, and
// nothing has read the type since dbf5054eb, so both are dropped rather than kept as unknown members.
[DroppedMembers("Property", "Target")]
public sealed class DefaultInputPort<T> : InputPort<T>, IDefaultInputPort
{
    public void SetPropertyAdapter(NodePropertyAdapter<T> property)
    {
        SetPropertyAdapterCore(property);
    }

    void IDefaultInputPort.SetPropertyAdapter(object property)
    {
        SetPropertyAdapter((NodePropertyAdapter<T>)property);
    }

    public NodePropertyAdapter<T>? GetProperty()
    {
        return PropertyAdapter;
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        PropertyAdapter?.SerializeValue(context);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        PropertyAdapter?.Deserialize(context);
    }
}
