namespace BeUtl;

#pragma warning disable IDE0032

public record class CorePropertyMetadata<T> : CorePropertyMetadata
{
    private T? _defaultValue;

    public T? DefaultValue
    {
        get => _defaultValue;
        init
        {
            _defaultValue = value;
            HasDefaultValue = true;
        }
    }

    public bool HasDefaultValue { get; private set; }

    public override void Merge(ICorePropertyMetadata baseMetadata, CoreProperty? property)
    {
        base.Merge(baseMetadata, property);

        if (!HasDefaultValue && baseMetadata is CorePropertyMetadata<T> baseT)
        {
            _defaultValue = baseT.DefaultValue;
            HasDefaultValue = true;
        }
    }

    protected internal override object? GetDefaultValue()
    {
        return HasDefaultValue ? _defaultValue : null;
    }
}

public interface ICorePropertyMetadata
{
    void Merge(ICorePropertyMetadata baseMetadata, CoreProperty? property);

    object? GetDefaultValue();
}

public abstract record class CorePropertyMetadata : ICorePropertyMetadata
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

    public virtual void Merge(ICorePropertyMetadata baseMetadata, CoreProperty? property)
    {
        if (baseMetadata is CorePropertyMetadata metadata1)
        {
            if (_observability == PropertyObservability.None)
            {
                _observability = metadata1.Observability;
            }

            if (string.IsNullOrEmpty(_serializeName))
            {
                _serializeName = metadata1.SerializeName;
            }

            if (_propertyFlags == PropertyFlags.None)
            {
                _propertyFlags = metadata1.PropertyFlags;
            }
        }
    }

    protected internal abstract object? GetDefaultValue();

    object? ICorePropertyMetadata.GetDefaultValue()
    {
        return GetDefaultValue();
    }
}
