﻿using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Serialization;

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
