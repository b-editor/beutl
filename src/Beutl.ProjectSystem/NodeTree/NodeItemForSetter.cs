using System.Text.Json.Nodes;

using Beutl.Media;

namespace Beutl.NodeTree;

public sealed class NodeItemForSetter<T> : NodeItem<T>
{
    public void SetProperty(SetterPropertyImpl<T> property)
    {
        Property = property;
        property.Setter.Invalidated += OnSetterInvalidated;
    }

    private void OnSetterInvalidated(object? sender, EventArgs e)
    {
        RaiseInvalidated(new RenderInvalidatedEventArgs(this));
    }

    public SetterPropertyImpl<T>? GetProperty()
    {
        return Property as SetterPropertyImpl<T>;
    }

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        GetProperty()?.ReadFromJson(json);
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        GetProperty()?.WriteToJson(json);
    }
}
