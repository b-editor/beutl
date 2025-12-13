using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

using Beutl.Media;

using ASolidColorBrush = Avalonia.Media.SolidColorBrush;
using AColor = Avalonia.Media.Color;

namespace Beutl.Controls.PropertyEditors;

public class GradingColorBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GradingColor gradingColor)
        {
            var beutlColor = gradingColor.ToColor();
            var avaloniaColor = AColor.FromArgb(beutlColor.A, beutlColor.R, beutlColor.G, beutlColor.B);
            return new ASolidColorBrush(avaloniaColor);
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
