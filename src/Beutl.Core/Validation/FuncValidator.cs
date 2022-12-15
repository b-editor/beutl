namespace Beutl.Validation;

public delegate bool ValueCoercer<T>(ValidationContext context, ref T? value);

public delegate string? ValueValidator<T>(ValidationContext context, T? value);

public sealed class FuncValidator<T> : IValidator<T>
{
    public ValueCoercer<T>? CoerceFunc { get; set; }

    public ValueValidator<T>? ValidateFunc { get; set; }

    public bool TryCoerce(ValidationContext context, ref T? value)
    {
        if (CoerceFunc != null)
        {
            return CoerceFunc(context, ref value);
        }
        else
        {
            return false;
        }
    }

    public string? Validate(ValidationContext context, T? value)
    {
        return ValidateFunc?.Invoke(context, value) ?? null;
    }
}
