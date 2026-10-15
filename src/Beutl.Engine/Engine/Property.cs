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

    public static IProperty<T> Create<T>(
        T defaultValue = default(T)!,
        IValidator<T>? validator = null)
    {
        var property = new SimpleProperty<T>(defaultValue, validator);

        return property;
    }

    /// <summary>
    /// Creates a property that takes no expression, for a value that has no meaning that varies with time,
    /// such as one that lays out a structure once.
    /// </summary>
    public static IProperty<T> CreateWithoutExpression<T>(
        T defaultValue = default(T)!,
        IValidator<T>? validator = null)
    {
        return new SimpleProperty<T>(defaultValue, validator) { SupportsExpression = false };
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
