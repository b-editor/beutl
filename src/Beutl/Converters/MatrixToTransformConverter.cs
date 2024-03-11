using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Beutl.Converters;

public sealed class MatrixToTransformConverter : IValueConverter
{
    public static readonly MatrixToTransformConverter Instance = new ();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Matrix matrix)
        {
            return new ImmutableTransform(matrix);
        }
        else
        {
            return BindingNotification.Null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ITransform t)
        {
            return t.Value;
        }
        else
        {
            return BindingNotification.Null;
        }
    }
}
