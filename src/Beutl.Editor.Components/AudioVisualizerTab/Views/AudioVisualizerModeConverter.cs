using Avalonia.Data.Converters;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

namespace Beutl.Editor.Components.AudioVisualizerTab.Views;

public sealed class AudioVisualizerModeConverter : IValueConverter
{
    public static readonly AudioVisualizerModeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AudioVisualizerMode mode)
        {
            if (parameter is string paramStr)
            {
                return Enum.TryParse(paramStr, out AudioVisualizerMode other) && other == mode;
            }
            return (int)mode;
        }
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int i && Enum.IsDefined(typeof(AudioVisualizerMode), i))
        {
            return (AudioVisualizerMode)i;
        }
        return AudioVisualizerMode.Waveform;
    }
}
