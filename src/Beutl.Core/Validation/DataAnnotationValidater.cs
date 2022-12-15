using System.ComponentModel.DataAnnotations;

namespace Beutl.Validation;

public sealed class DataAnnotationValidater<T> : IValidator<T>
{
    public DataAnnotationValidater()
    {
    }

    public DataAnnotationValidater(ValidationAttribute? attribute)
    {
        Attribute = attribute;
    }

    public ValidationAttribute? Attribute { get; set; }

    public bool TryCoerce(ValidationContext context, ref T? value)
    {
        return false;
    }

    public string? Validate(ValidationContext context, T? value)
    {
        if (Attribute == null)
        {
            return null;
        }

        if (!Attribute.RequiresValidationContext)
        {
            if (!Attribute.IsValid(value))
            {
                return Attribute.FormatErrorMessage(context.Property?.Name ?? typeof(T).Name);
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
