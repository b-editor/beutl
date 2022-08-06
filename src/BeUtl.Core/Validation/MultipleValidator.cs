namespace BeUtl.Validation;

public sealed class MultipleValidator<T> : IValidator<T>
{
    public MultipleValidator(IValidator<T>[] items)
    {
        Items = items;
    }

    public IValidator<T>[] Items { get; }

    public T? Coerce(ICoreObject? obj, T? value)
    {
        foreach (IValidator<T> item in Items)
        {
            value = item.Coerce(obj, value);
        }

        return value;
    }

    public bool Validate(ICoreObject? obj, T? value)
    {
        foreach (IValidator<T> item in Items)
        {
            if (!item.Validate(obj, value))
            {
                return false;
            }
        }

        return true;
    }
}
