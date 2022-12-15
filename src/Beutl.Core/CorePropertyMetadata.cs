using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json.Serialization;

using Beutl.Validation;

namespace Beutl;

public class CorePropertyMetadata<T> : CorePropertyMetadata
{
    private Optional<T> _defaultValue;

    public CorePropertyMetadata(
        string? serializeName = null,
        Optional<PropertyFlags> propertyFlags = default,
        Optional<T> defaultValue = default,
        IValidator<T>? validator = null,
        JsonConverter<T>? jsonConverter = null,
        DisplayAttribute? displayAttribute = null)
        : base(serializeName, propertyFlags, displayAttribute)
    {
        _defaultValue = defaultValue;
        Validator = validator;
        JsonConverter = jsonConverter;
    }

    public T DefaultValue => _defaultValue.GetValueOrDefault()!;

    public bool HasDefaultValue => _defaultValue.HasValue;

    public IValidator<T>? Validator { get; private set; }

    public JsonConverter<T>? JsonConverter { get; private set; }

    public override Type PropertyType => typeof(T);

    public override void Merge(ICorePropertyMetadata baseMetadata, CoreProperty? property)
    {
        base.Merge(baseMetadata, property);

        if (baseMetadata is CorePropertyMetadata<T> baseT)
        {
            if (!HasDefaultValue)
            {
                _defaultValue = baseT.DefaultValue;
            }

            Validator ??= baseT.Validator;

            JsonConverter ??= baseT.JsonConverter;
        }
    }

    public TValidator? FindValidator<TValidator>()
        where TValidator : IValidator<T>
    {
        if (Validator is TValidator validator1)
        {
            return validator1;
        }
        else if (Validator is TuppleValidator<T> validator2)
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
        else if (Validator is MultipleValidator<T> validator5)
        {
            foreach (IValidator<T> item in validator5.Items)
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
        return HasDefaultValue ? _defaultValue.Value : null;
    }

    protected internal override IValidator? GetValidator()
    {
        return Validator;
    }
}

public interface ICorePropertyMetadata
{
    Type PropertyType { get; }

    DisplayAttribute? DisplayAttribute { get; }

    void Merge(ICorePropertyMetadata baseMetadata, CoreProperty? property);

    object? GetDefaultValue();

    IValidator? GetValidator();
}

public abstract class CorePropertyMetadata : ICorePropertyMetadata
{
    private Optional<PropertyFlags> _propertyFlags;

    protected CorePropertyMetadata(string? serializeName, Optional<PropertyFlags> propertyFlags, DisplayAttribute? displayAttribute = null)
    {
        SerializeName = serializeName;
        _propertyFlags = propertyFlags;
        DisplayAttribute = displayAttribute;
    }

    public string? SerializeName { get; private set; }

    public PropertyFlags PropertyFlags => _propertyFlags.GetValueOrDefault();

    public abstract Type PropertyType { get; }

    public DisplayAttribute? DisplayAttribute { get; private set; }

    public virtual void Merge(ICorePropertyMetadata baseMetadata, CoreProperty? property)
    {
        if (baseMetadata is CorePropertyMetadata metadata1)
        {
            if (string.IsNullOrEmpty(SerializeName))
            {
                SerializeName = metadata1.SerializeName;
            }

            if (!_propertyFlags.HasValue)
            {
                _propertyFlags = metadata1._propertyFlags;
            }

            if (DisplayAttribute == null)
            {
                DisplayAttribute = metadata1.DisplayAttribute;
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
