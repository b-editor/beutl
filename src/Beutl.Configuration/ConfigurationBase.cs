using System.Reactive;
using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl.Configuration;

public abstract class ConfigurationBase : CoreObject
{
    public event EventHandler? ConfigurationChanged;

    protected static void AffectsConfig<T>(params CoreProperty[] properties)
        where T : ConfigurationBase
    {
        foreach (CoreProperty? item in properties)
        {
            item.Changed.Subscribe(e =>
            {
                if (e.Sender is T s)
                {
                    s.OnChanged();

                    if (e.OldValue is ConfigurationBase oldAffectsRender)
                        oldAffectsRender.ConfigurationChanged -= s.OnConfigurationChanged;

                    if (e.NewValue is ConfigurationBase newAffectsRender)
                        newAffectsRender.ConfigurationChanged += s.OnConfigurationChanged;
                }
            });
        }
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        OnChanged();
    }

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
