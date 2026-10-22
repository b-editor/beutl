using System.ComponentModel.DataAnnotations;
using Beutl.Validation;

namespace Beutl;

internal static class PropertyValidators
{
    public static IValidator<T>[] FromAttributes<T>(IEnumerable<ValidationAttribute> attributes)
    {
        return attributes
            .Select(CorePropertyMetadata<T>.ConvertValidator)
            .ToArray();
    }
}
