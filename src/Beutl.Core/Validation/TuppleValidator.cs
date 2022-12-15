namespace Beutl.Validation;

public sealed class TuppleValidator<T> : IValidator<T>
{
    public TuppleValidator(IValidator<T> first, IValidator<T> second)
    {
        First = first;
        Second = second;
    }

    public IValidator<T> First { get; }

    public IValidator<T> Second { get; }

    public bool TryCoerce(ValidationContext context, ref T? value)
    {
        T? tmp = value;
        if (First.TryCoerce(context, ref tmp) && Second.TryCoerce(context, ref tmp))
        {
            value = tmp;
            return true;
        }
        else
        {
            return false;
        }
    }

    public string? Validate(ValidationContext context, T? value)
    {
        string? ms1 = First.Validate(context, value);
        string? ms2 = Second.Validate(context, value);
        if (ms1 != null && ms2 != null)
        {
            return $"{ms1}\n{ms2}";
        }
        else
        {
            return ms1 ?? ms2;
        }
    }
}
