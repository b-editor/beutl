using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.NodeTree;

public sealed class InputSocketForSetter<T> : InputSocket<T>, IInputSocketForSetter
{
    public void SetPropertyAdapter(SetterAdapter<T> property)
    {
        Property = property;
        property.Setter.Invalidated += OnSetterInvalidated;
    }

    void IInputSocketForSetter.SetPropertyAdapter(object property)
    {
        var obj = (SetterAdapter<T>)property;
        Property = obj;
        obj.Setter.Invalidated += OnSetterInvalidated;
    }

    private void OnSetterInvalidated(object? sender, EventArgs e)
    {
        RaiseInvalidated(new RenderInvalidatedEventArgs(this));
    }

    public SetterAdapter<T>? GetProperty()
    {
        return Property as SetterAdapter<T>;
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
