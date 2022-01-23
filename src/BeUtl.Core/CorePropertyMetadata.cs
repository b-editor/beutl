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
    private PropertyFlags _designerFlags;

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

    public PropertyFlags DesignerFlags
    {
        get => _designerFlags;
        init => _designerFlags = value;
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

        if (_designerFlags == PropertyFlags.None)
        {
            _designerFlags = baseMetadata.DesignerFlags;
        }
    }

    protected internal abstract object? GetDefaultValue();
}
