namespace BeUtl.Validation;

public abstract class RangeValidator<T> : IValidator<T>
{
    public T? Maximum { get; set; }

    public T? Minimum { get; set; }

    public abstract T? Coerce(ICoreObject obj, T? value);

    public abstract bool Validate(ICoreObject obj, T? value);
}
