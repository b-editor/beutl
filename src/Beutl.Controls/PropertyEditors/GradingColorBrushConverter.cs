using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Beutl.Media;
using ASolidColorBrush = Avalonia.Media.SolidColorBrush;

namespace Beutl.Controls.PropertyEditors;

public class GradingColorBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GradingColor gradingColor)
        {
            return new ASolidColorBrush(gradingColor.ToColor().ToAvaColor());
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
