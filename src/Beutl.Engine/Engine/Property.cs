using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Beutl.Animation;
using Beutl.Media;
using Beutl.Validation;

namespace Beutl.Engine;

public static class Property
{
    public static IProperty<T> CreateAnimatable<T>(
        T defaultValue = default(T)!,
        IValidator<T>? validator = null)
    {
        var property = new AnimatableProperty<T>(defaultValue, validator);

        return property;
    }

    public static IProperty<T> CreateAnimatable<T>(
        T defaultValue,
        params ValidationAttribute[] validationAttributes)
    {
        var validator = validationAttributes.Length > 0
            ? new MultipleValidator<T>(validationAttributes
                .Select(CorePropertyMetadata<T>.ConvertValidator)
                .ToArray())
            : null;

        return CreateAnimatable(defaultValue, validator);
    }

    public static IProperty<T> Create<T>(
        T defaultValue = default(T)!,
        IValidator<T>? validator = null)
    {
        var property = new SimpleProperty<T>(defaultValue, validator);

        return property;
    }

    public static IProperty<T> Create<T>(
        T defaultValue,
        params ValidationAttribute[] validationAttributes)
    {
        var validator = validationAttributes.Length > 0
            ? new MultipleValidator<T>(validationAttributes
                .Select(CorePropertyMetadata<T>.ConvertValidator)
                .ToArray())
            : null;

        return Create(defaultValue, validator);
    }

    public static IListProperty<T> CreateList<T>()
    {
        return new ListProperty<T>();
    }
}
