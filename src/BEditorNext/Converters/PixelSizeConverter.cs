using System.Globalization;

using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace BEditorNext.Converters;

public sealed class PixelSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is PixelSize size)
        {
            return size.ToString();
        }
        else
        {
            return new BindingNotification(value);
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            try
            {
                return PixelSize.Parse(str);
            }
            catch
            {
                return new BindingNotification(new FormatException($"'{str}' is not a valid."), BindingErrorType.Error, str);
            }
        }

        return new BindingNotification(value);
    }
}
