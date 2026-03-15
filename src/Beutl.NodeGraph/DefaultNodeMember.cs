using Beutl.Serialization;

namespace Beutl.NodeGraph;

public sealed class DefaultNodeMember<T> : NodeMember<T>
{
    public void SetProperty(NodePropertyAdapter<T> property)
    {
        Property = property;
        property.Edited += OnAdapterEdited;
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
