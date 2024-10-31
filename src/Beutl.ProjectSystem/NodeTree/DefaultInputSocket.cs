using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.NodeTree;

public sealed class DefaultInputSocket<T> : InputSocket<T>, IDefaultInputSocket
{
    public void SetPropertyAdapter(NodePropertyAdapter<T> property)
    {
        Property = property;
        property.Invalidated += OnAdapterInvalidated;
    }

    void IDefaultInputSocket.SetPropertyAdapter(object property)
    {
        var obj = (NodePropertyAdapter<T>)property;
        Property = obj;
        obj.Invalidated += OnAdapterInvalidated;
    }

    private void OnAdapterInvalidated(object? sender, EventArgs e)
    {
        RaiseInvalidated(new RenderInvalidatedEventArgs(this));
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
