using Beutl.Serialization;

namespace Beutl.NodeGraph;

// Earlier builds also wrote the property name and the adapter type here. The port finds its property by Name, and
// nothing has read the type since dbf5054eb, so both are dropped rather than kept as unknown members.
[DroppedMembers("Property", "Target")]
public sealed class DefaultInputPort<T> : InputPort<T>, IDefaultInputPort
{
    public void SetPropertyAdapter(NodePropertyAdapter<T> property)
    {
        Property = property;
        property.Edited += OnAdapterEdited;
    }

    void IDefaultInputPort.SetPropertyAdapter(object property)
    {
        SetPropertyAdapter((NodePropertyAdapter<T>)property);
    }

    private void OnAdapterEdited(object? sender, EventArgs e)
    {
        RaiseEdited();
    }

    public NodePropertyAdapter<T>? GetProperty()
    {
        return Property as NodePropertyAdapter<T>;
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        GetProperty()?.SerializeValue(context);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        GetProperty()?.Deserialize(context);
    }
}
