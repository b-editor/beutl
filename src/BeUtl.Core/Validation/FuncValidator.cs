namespace BeUtl.Validation;

public sealed class FuncValidator<T> : IValidator<T>
{
    public Func<ICoreObject, T?, T?>? CoerceFunc { get; set; }

    public Func<ICoreObject, T?, bool>? ValidateFunc { get; set; }

    public T? Coerce(ICoreObject obj, T? value)
    {
        if (CoerceFunc != null)
        {
            return CoerceFunc(obj, value);
        }
        else
        {
            return value;
        }
    }

    public bool Validate(ICoreObject obj, T? value)
    {
        return ValidateFunc?.Invoke(obj, value) ?? true;
    }
}
