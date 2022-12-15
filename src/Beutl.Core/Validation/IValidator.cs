namespace Beutl.Validation;

public interface IValidator
{
    string? Validate(ValidationContext context, object? value);

    bool TryCoerce(ValidationContext context, ref object? value);
}

public interface IValidator<T> : IValidator
{
    string? Validate(ValidationContext context, T? value);

    bool TryCoerce(ValidationContext context, ref T? value);

    string? IValidator.Validate(ValidationContext context, object? value)
    {
        return Validate(context, (T?)value);
    }

    bool IValidator.TryCoerce(ValidationContext context, ref object? value)
    {
        T? typed = value is T t ? t : default;
        bool result = TryCoerce(context, ref typed);
        value = typed;
        return result;
    }
}
