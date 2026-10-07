using System.ComponentModel.DataAnnotations;
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
            ? new MultipleValidator<T>(ConvertValidators<T>(validationAttributes))
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
            ? new MultipleValidator<T>(ConvertValidators<T>(validationAttributes))
            : null;

        return Create(defaultValue, validator);
    }

    public static IListProperty<T> CreateList<T>()
    {
        return new ListProperty<T>();
    }

    internal static IValidator<T>[] ConvertValidators<T>(IEnumerable<ValidationAttribute> attributes)
    {
        return attributes
            .Select(CorePropertyMetadata<T>.ConvertValidator)
            .ToArray();
    }

    public static string GetLocalizedName(IProperty property)
    {
        var displayAttr = property.GetAttributes()?.OfType<DisplayAttribute>().FirstOrDefault();
        return displayAttr?.GetName() ?? property.Name;
    }
}
