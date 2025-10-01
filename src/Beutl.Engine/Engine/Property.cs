using System.ComponentModel.DataAnnotations;
using Beutl.Validation;

namespace Beutl.Engine;

public static class Property
{
    public static IProperty<T> CreateAnimatable<T>(
        string name,
        T defaultValue = default(T)!,
        IValidator<T>? validator = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Property name cannot be null or empty", nameof(name));

        var property = new AnimatableProperty<T>(name, defaultValue, validator);

        return property;
    }

    public static IProperty<T> CreateAnimatable<T>(
        string name,
        T defaultValue,
        params ValidationAttribute[] validationAttributes)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Property name cannot be null or empty", nameof(name));

        var validator = validationAttributes.Length > 0
            ? new MultipleValidator<T>(validationAttributes
                .Select(CorePropertyMetadata<T>.ConvertValidator)
                .ToArray())
            : null;

        return CreateAnimatable(name, defaultValue, validator);
    }

    public static IProperty<T> Create<T>(
        string name,
        T defaultValue = default(T)!,
        IValidator<T>? validator = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Property name cannot be null or empty", nameof(name));

        var property = new SimpleProperty<T>(name, defaultValue, validator);

        return property;
    }

    public static IProperty<T> Create<T>(
        string name,
        T defaultValue,
        params ValidationAttribute[] validationAttributes)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Property name cannot be null or empty", nameof(name));

        var validator = validationAttributes.Length > 0
            ? new MultipleValidator<T>(validationAttributes
                .Select(CorePropertyMetadata<T>.ConvertValidator)
                .ToArray())
            : null;

        return Create(name, defaultValue, validator);
    }
}
