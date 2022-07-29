using System.Text.Json.Serialization;

using BeUtl.Validation;

namespace BeUtl;

#pragma warning disable IDE0032

public record class CorePropertyMetadata<T> : CorePropertyMetadata
{
    private T? _defaultValue;
    private IValidator<T>? _validator;
    private JsonConverter<T>? _jsonConverter;

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

    public IValidator<T>? Validator
    {
        get => _validator;
        init => _validator = value;
    }

    public JsonConverter<T>? JsonConverter
    {
        get => _jsonConverter;
        init => _jsonConverter = value;
    }

    public override void Merge(ICorePropertyMetadata baseMetadata, CoreProperty? property)
    {
        base.Merge(baseMetadata, property);

        if (baseMetadata is CorePropertyMetadata<T> baseT)
        {
            if (!HasDefaultValue)
            {
                _defaultValue = baseT.DefaultValue;
                HasDefaultValue = true;
            }

            if (_validator == null)
            {
                _validator = baseT.Validator;
            }

            if (_jsonConverter == null)
            {
                _jsonConverter = baseT.JsonConverter;
            }
        }
    }

    public TValidator? FindValidator<TValidator>()
        where TValidator : IValidator<T>
    {
        if (_validator is TValidator validator1)
        {
            return validator1;
        }
        else if (_validator is TuppleValidator<T> validator2)
        {
            if (validator2.First is TValidator validator3)
            {
                return validator3;
            }
            else if (validator2.Second is TValidator validator4)
            {
                return validator4;
            }
        }
        else if (_validator is MultipleValidator<T> validator5)
        {
            foreach (var item in validator5.Items)
            {
                if (item is TValidator validator6)
                {
                    return validator6;
                }
            }
        }

        return default;
    }

    protected internal override object? GetDefaultValue()
    {
        return HasDefaultValue ? _defaultValue : null;
    }

    protected internal override IValidator? GetValidator()
    {
        return Validator;
    }
}

public interface ICorePropertyMetadata
{
    void Merge(ICorePropertyMetadata baseMetadata, CoreProperty? property);

    object? GetDefaultValue();

    IValidator? GetValidator();
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

    protected internal abstract IValidator? GetValidator();

    object? ICorePropertyMetadata.GetDefaultValue()
    {
        return GetDefaultValue();
    }

    IValidator? ICorePropertyMetadata.GetValidator()
    {
        return GetValidator();
    }
}
