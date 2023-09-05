using System.Text.Json.Nodes;

namespace Beutl.Configuration;

public abstract class ConfigurationBase : CoreObject
{
    public event EventHandler? ConfigurationChanged;

    protected void OnChanged()
    {
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        json.Remove(nameof(Id));
        json.Remove(nameof(Name));
    }
}
