using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Serialization;

using Beutl.Validation;

namespace Beutl;

public class CorePropertyMetadata<T> : CorePropertyMetadata
{
    private Optional<T> _defaultValue;

    public CorePropertyMetadata(
        Optional<T> defaultValue = default,
        bool shouldSerialize = true,
        params Attribute[] attributes)
        : base(shouldSerialize, attributes)
    {
        _defaultValue = defaultValue;
        UpdatedAttributes();
    }

    public T DefaultValue => _defaultValue.GetValueOrDefault()!;

    public bool HasDefaultValue => _defaultValue.HasValue;

    public IValidator<T>? Validator { get; private set; }

    public JsonConverter<T>? JsonConverter { get; private set; }

    public override Type PropertyType => typeof(T);

    private static IValidator<T> ConvertValidator(ValidationAttribute att)
    {
        switch (att)
        {
            case RangeAttribute rangeAttribute:
                Type propType = typeof(T);
                Type[] interfaces = propType.GetInterfaces();
                if (propType.IsValueType
                    && interfaces.Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(INumber<>))
                    && interfaces.Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IMinMaxValue<>)))
                {
                    Type validatorType = typeof(RangeDataAnnotationValidater<>);
                    if (Activator.CreateInstance(validatorType, rangeAttribute) is IValidator<T> validator)
                    {
                        return validator;
                    }
                    else
                    {
                        goto default;
                    }
                }
                else
                {
                    goto default;
                }

            default:
                return new DataAnnotationValidater<T>(att);
        }
    }

    private void UpdatedAttributes()
    {
        if (Attributes != null)
        {
            IValidator<T>[] validations = Attributes.OfType<ValidationAttribute>()
                .Select(ConvertValidator)
                .ToArray();

            Validator = new MultipleValidator<T>(validations);

            JsonConverterAttribute? jsonConverter = Attributes.OfType<JsonConverterAttribute>().FirstOrDefault();
            if (jsonConverter is { ConverterType: { } })
            {
                JsonConverter = JsonHelper.GetOrCreateConverterInstance(jsonConverter.ConverterType) as JsonConverter<T>;
            }
            else
            {
                JsonConverter = null;
            }
        }
    }

    public override void Merge(ICorePropertyMetadata baseMetadata, CoreProperty? property)
    {
        base.Merge(baseMetadata, property);

        if (baseMetadata is CorePropertyMetadata<T> baseT)
        {
            if (!HasDefaultValue)
            {
                _defaultValue = baseT.DefaultValue;
            }

            UpdatedAttributes();
        }
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
