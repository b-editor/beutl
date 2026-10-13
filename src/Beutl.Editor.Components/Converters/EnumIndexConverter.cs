using Avalonia.Data.Converters;

namespace Beutl.Editor.Components.Converters;

// Converts an enum to a ComboBox index, or, with a member name as the parameter, to whether it is that member.
public sealed class EnumIndexConverter<TEnum>(TEnum fallback) : IValueConverter
    where TEnum : struct, Enum
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TEnum item)
        {
            if (parameter is string name)
            {
                return Enum.TryParse(name, out TEnum other) && EqualityComparer<TEnum>.Default.Equals(item, other);
            }

            return (int)(object)item;
        }

        return parameter != null ? false : 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index && Enum.IsDefined(typeof(TEnum), index))
        {
            return (TEnum)Enum.ToObject(typeof(TEnum), index);
        }

        return fallback;
    }
}
