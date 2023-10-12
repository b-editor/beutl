// https://github.com/ymg2006/FluentAvalonia.ProgressRing/blob/49bf3a2287033decb36ed81f59f5e18cb9333f39/FluentAvalonia.ProgressRing/Converters/FitSquarelyWithinAspectRatioConverter.cs

using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;

namespace Beutl.Controls;

public class FitSquarelyWithinAspectRatioConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        Rect bounds = (Rect)value;
        return Math.Min(bounds.Width, bounds.Height);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
