using System.Reactive;
using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Configuration;

public abstract class ConfigurationBase : CoreObject
{
    private static ILogger? s_logger;
    private List<(string Property, Exception Exception)>? _deserializeFailures;

    public event EventHandler? ConfigurationChanged;

    private static ILogger Logger => Log.GetLoggerOnceConfigured(ref s_logger, typeof(ConfigurationBase));

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

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Id), Unit.Default);
        context.SetValue(nameof(Name), Unit.Default);
    }

    // Reads each setting on its own, so a value of the wrong type keeps that setting's default and the
    // others are still read. Settings hold no references, which CoreObject.Deserialize would resolve.
    public override void Deserialize(ICoreSerializationContext context)
    {
        foreach (CoreProperty property in PropertyRegistry.GetRegistered(GetType()))
        {
            try
            {
                Optional<object?> value = property.RouteDeserialize(context);
                if (value.HasValue)
                    SetValue(property, value.Value);
            }
            catch (Exception ex)
            {
                (_deserializeFailures ??= []).Add((property.Name, ex));
                // Settings read before logging is set up are reported through GlobalConfiguration.RestoreFailures.
                Logger.LogWarning(ex, "Could not read {Setting} of {Section}, so it keeps its default.", property.Name, GetType().Name);
            }
        }
    }

    internal List<(string Property, Exception Exception)> TakeDeserializeFailures()
    {
        List<(string Property, Exception Exception)> failures = _deserializeFailures ?? [];
        _deserializeFailures = null;
        return failures;
    }
}
