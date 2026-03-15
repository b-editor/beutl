using Beutl.Serialization;

namespace Beutl.NodeGraph;

public sealed class DefaultInputPort<T> : InputPort<T>, IDefaultInputPort
{
    public void SetPropertyAdapter(NodePropertyAdapter<T> property)
    {
        Property = property;
        property.Edited += OnAdapterEdited;
    }

    void IDefaultInputPort.SetPropertyAdapter(object property)
    {
        var obj = (NodePropertyAdapter<T>)property;
        Property = obj;
        obj.Edited += OnAdapterEdited;
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
        GetProperty()?.Serialize(context);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        GetProperty()?.Deserialize(context);
    }
}
