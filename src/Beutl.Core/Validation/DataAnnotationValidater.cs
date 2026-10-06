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
        return DataAnnotationValidation.ValidateWithAttribute(Attribute, context, value, typeof(T));
    }
}
