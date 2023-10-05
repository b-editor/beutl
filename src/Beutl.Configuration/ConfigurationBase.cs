using System.Reactive;
using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl.Configuration;

public abstract class ConfigurationBase : CoreObject
{
    public event EventHandler? ConfigurationChanged;

    protected void OnChanged()
    {
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    [ObsoleteSerializationApi]
    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        json.Remove(nameof(Id));
        json.Remove(nameof(Name));
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Id), Unit.Default);
        context.SetValue(nameof(Name), Unit.Default);
    }
}
