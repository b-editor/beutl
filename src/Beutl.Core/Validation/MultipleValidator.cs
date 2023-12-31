using System.Text;

namespace Beutl.Validation;

public sealed class MultipleValidator<T>(IValidator<T>[] items) : IValidator<T>
{
    public IValidator<T>[] Items { get; } = items;

    public bool TryCoerce(ValidationContext context, ref T? value)
    {
        T? tmp = value;
        foreach (IValidator<T> item in Items)
        {
            if (!item.TryCoerce(context, ref tmp))
            {
                return false;
            }
        }

        value = tmp;
        return true;
    }

    public string? Validate(ValidationContext context, T? value)
    {
        StringBuilder? sb = null;
        foreach (IValidator<T> item in Items)
        {
            string? ms = item.Validate(context, value);
            if (ms != null)
            {
                sb ??= new StringBuilder();
                sb.AppendLine(ms);
            }
        }

        return sb?.ToString();
    }
}
