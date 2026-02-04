using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Beutl.Utilities;

namespace Beutl.Editor.Components.Converters;

public sealed class AvaloniaThicknessConverter : IValueConverter
{
    public static readonly AvaloniaThicknessConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double valued)
        {
            if (parameter is string f)
            {
                var tokenizer = new RefStringTokenizer(f);
                Thickness thickness = default;

                while (tokenizer.TryReadString(out ReadOnlySpan<char> segment))
                {
                    thickness += segment switch
                    {
                        "Left" => new Thickness(valued, 0, 0, 0),
                        "Top" => new Thickness(0, valued, 0, 0),
                        "Right" => new Thickness(0, 0, valued, 0),
                        "Bottom" => new Thickness(0, 0, 0, valued),
                        _ => new Thickness(valued),
                    };
                }

                return thickness;
            }
            else
            {
                return new Thickness(valued);
            }
        }
        else
        {
            return (Thickness)default;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return new BindingNotification(value);
    }
}
