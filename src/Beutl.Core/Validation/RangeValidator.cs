namespace Beutl.Validation;

public abstract class RangeValidator<T> : IValidator<T>
{
    public T? Maximum { get; set; }

    public T? Minimum { get; set; }

    public abstract bool TryCoerce(ValidationContext context, ref T? value);

    public abstract string? Validate(ValidationContext context, T? value);
}
