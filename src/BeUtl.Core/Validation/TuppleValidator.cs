namespace BeUtl.Validation;

public sealed class TuppleValidator<T> : IValidator<T>
{
    public TuppleValidator(IValidator<T> first, IValidator<T> second)
    {
        First = first;
        Second = second;
    }

    public IValidator<T> First { get; }

    public IValidator<T> Second { get; }

    public T? Coerce(ICoreObject obj, T? value)
    {
        return Second.Coerce(obj, First.Coerce(obj, value));
    }

    public bool Validate(ICoreObject obj, T? value)
    {
        return First.Validate(obj, value) && Second.Validate(obj, value);
    }
}
