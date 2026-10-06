using System.ComponentModel.DataAnnotations;

namespace Beutl.Validation;

internal static class DataAnnotationValidation
{
    // The error message attribute gives for value, named after the property or else valueType.
    public static string? ValidateWithAttribute<T>(
        ValidationAttribute? attribute,
        ValidationContext context,
        T? value,
        Type valueType)
    {
        if (attribute == null)
        {
            return null;
        }

        if (!attribute.RequiresValidationContext)
        {
            if (!attribute.IsValid(value))
            {
                return attribute.FormatErrorMessage(context.Property?.Name ?? valueType.Name);
            }
            else
            {
                return null;
            }
        }
        else
        {
            throw new InvalidOperationException("System.ComponentModel.DataAnnotations.ValidationContext required validation is not yet supported.");
        }
    }
}
