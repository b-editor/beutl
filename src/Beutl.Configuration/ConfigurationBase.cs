using System.ComponentModel;
using System.Reactive;
using System.Reflection;
using System.Text.Json;
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
                }
            });
        }
    }

    protected void OnChanged()
    {
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    // A setting raises PropertyChanged under its name, as its CoreProperty field did.
    internal override void OnPropertyValueChanged(IProperty property)
    {
        if (!IsOwnField(property))
            OnPropertyChanged(new PropertyChangedEventArgs(property.Name));
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
        foreach (IProperty property in RegisteredProperties)
        {
            if (IsOwnField(property) || !IsAutoSerialized(property))
                continue;

            try
            {
                string name = FormerPersistedNames.ResolveMemberKey(context, property.Name, property.GetAttributes());
                // A JSON null reads as default(T) without an error, which would leave, say, UICulture null.
                if (IsJsonNull(context, name) && !AcceptsNull(property.ValueType, property.Name))
                    throw new JsonException($"{property.Name} cannot be null.");

                property.DeserializeValue(context);
            }
            catch (Exception ex)
            {
                RecordDeserializeFailure(property.Name, ex);
            }
        }

        foreach (CoreProperty property in PropertyRegistry.GetRegistered(GetType()))
        {
            try
            {
                Optional<object?> value = property.RouteDeserialize(context);
                if (!value.HasValue)
                    continue;

                // A JSON null reads as default(T) without an error, which would leave, say, UICulture null.
                if (IsJsonNull(context, property.GetReadName(context)) && !AcceptsNull(property.PropertyType, property.Name))
                    throw new JsonException($"{property.Name} cannot be null.");

                SetValue(property, value.Value);
            }
            catch (Exception ex)
            {
                RecordDeserializeFailure(property.Name, ex);
            }
        }
    }

    // Asks for this key alone: taking the whole JSON object would count every key as read, so the section would not
    // keep the settings a newer build added, and saving it would drop them.
    private static bool IsJsonNull(ICoreSerializationContext context, string name)
        => context is IJsonSerializationContext json
           && json.Contains(name)
           && json.GetNode(name) is null;

    private bool AcceptsNull(Type valueType, string name)
    {
        if (valueType.IsValueType)
            return Nullable.GetUnderlyingType(valueType) is not null;

        // The property's own annotation; one without a CLR property, or without annotations, takes null.
        PropertyInfo? clrProperty = GetType().GetProperty(name);
        return clrProperty is null
               || new NullabilityInfoContext().Create(clrProperty).WriteState != NullabilityState.NotNull;
    }

    // Reads a value a section stores itself, with the same tolerance. False when it cannot be read,
    // which a section answers by keeping its default; a missing value reads as default(T) and true.
    private protected bool TryReadValue<T>(ICoreSerializationContext context, string name, out T? value)
    {
        try
        {
            value = context.GetValue<T>(name);
            return true;
        }
        catch (Exception ex)
        {
            RecordDeserializeFailure(name, ex);
            value = default;
            return false;
        }
    }

    private void RecordDeserializeFailure(string name, Exception exception)
    {
        (_deserializeFailures ??= []).Add((name, exception));
        // Settings read before logging is set up are reported through GlobalConfiguration.RestoreFailures.
        Logger.LogWarning(exception, "Could not read {Setting} of {Section}, so it keeps its default.", name, GetType().Name);
    }

    internal List<(string Property, Exception Exception)> TakeDeserializeFailures()
    {
        List<(string Property, Exception Exception)> failures = _deserializeFailures ?? [];
        _deserializeFailures = null;
        return failures;
    }
}
