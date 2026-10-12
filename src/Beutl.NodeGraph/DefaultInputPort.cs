using Beutl.Serialization;

namespace Beutl.NodeGraph;

// Earlier builds also wrote the property name here, which the port does not read: it finds its property by Name. A
// stored name is dropped rather than kept as an unknown member.
[DroppedMembers("Property")]
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
