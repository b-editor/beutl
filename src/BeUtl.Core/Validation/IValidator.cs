namespace BeUtl.Validation;

public interface IValidator
{
    bool Validate(ICoreObject? obj, object? value);

    object? Coerce(ICoreObject? obj, object? value);
}

public interface IValidator<T> : IValidator
{
    bool Validate(ICoreObject? obj, T? value);

    T? Coerce(ICoreObject? obj, T? value);

    bool IValidator.Validate(ICoreObject? obj, object? value)
    {
        return Validate(obj, (T?)value);
    }

    object? IValidator.Coerce(ICoreObject? obj, object? value)
    {
        return Coerce(obj, (T?)value);
    }
}
