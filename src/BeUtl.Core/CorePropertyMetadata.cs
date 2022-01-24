namespace BeUtl;

#pragma warning disable IDE0032

public class CorePropertyMetadata<T> : CorePropertyMetadata
{
    private T? _defaultValue;

    public T? DefaultValue
    {
        get => _defaultValue;
        init => _defaultValue = value;
    }

    public override void Merge(CorePropertyMetadata baseMetadata, CoreProperty property)
    {
        base.Merge(baseMetadata, property);

        if (_defaultValue == null && baseMetadata is CorePropertyMetadata<T> baseT)
        {
            _defaultValue = baseT.DefaultValue;
        }
    }

    protected internal override object? GetDefaultValue()
    {
        return _defaultValue;
    }
}

public abstract class CorePropertyMetadata
{
    private string? _serializeName;
    private PropertyObservability _observability;
    private PropertyFlags _propertyFlags;

    public string? SerializeName
    {
        get => _serializeName;
        init => _serializeName = value;
    }

    public PropertyObservability Observability
    {
        get => _observability;
        init => _observability = value;
    }

    public PropertyFlags PropertyFlags
    {
        get => _propertyFlags;
        init => _propertyFlags = value;
    }

    public virtual void Merge(CorePropertyMetadata baseMetadata, CoreProperty property)
    {
        if (_observability == PropertyObservability.None)
        {
            _observability = baseMetadata.Observability;
        }

        if (string.IsNullOrEmpty(_serializeName))
        {
            _serializeName = baseMetadata.SerializeName;
        }

        if (_propertyFlags == PropertyFlags.None)
        {
            _propertyFlags = baseMetadata.PropertyFlags;
        }
    }

    protected internal abstract object? GetDefaultValue();
}
