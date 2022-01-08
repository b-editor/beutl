namespace BEditorNext;

public class CorePropertyMetadata
{
    private readonly Dictionary<string, object> _options;

    public CorePropertyMetadata(object? defaultValue, PropertyObservability observability, Dictionary<string, object> options)
    {
        DefaultValue = defaultValue;
        Observability = observability;
        _options = options;
    }

    public object? DefaultValue { get; private set; }

    public PropertyObservability Observability { get; private set; }

    public IReadOnlyDictionary<string, object> Options => _options;

    public virtual T GetValue<T>(string key)
    {
        if (!Options.ContainsKey(key))
        {
            throw new KeyNotFoundException();
        }

        return (T)Options[key];
    }

    public virtual T? GetValueOrDefault<T>(string key)
    {
        if (!Options.ContainsKey(key))
        {
            return default;
        }

        return (T)Options[key];
    }

    public virtual T GetValueOrDefault<T>(string key, T defaltValue)
    {
        if (!Options.ContainsKey(key))
        {
            return defaltValue;
        }

        return (T)Options[key];
    }

    public virtual void Merge(CorePropertyMetadata baseMetadata, CoreProperty property)
    {
        foreach (KeyValuePair<string, object> item in baseMetadata.Options)
        {
            if (!Options.ContainsKey(item.Key))
            {
                _options.Add(item.Key, item.Value);
            }
        }

        if (DefaultValue == null)
        {
            DefaultValue = baseMetadata.DefaultValue;
        }

        if (Observability == PropertyObservability.None)
        {
            Observability = baseMetadata.Observability;
        }
    }
}
