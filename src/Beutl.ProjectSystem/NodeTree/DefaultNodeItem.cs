using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.NodeTree;

public sealed class DefaultNodeItem<T> : NodeItem<T>
{
    public void SetProperty(NodePropertyAdapter<T> property)
    {
        Property = property;
        property.Edited += OnAdapterEdited;
    }

    private void OnAdapterEdited(object? sender, EventArgs e)
    {
        RaiseEdited(new RenderInvalidatedEventArgs(this));
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
