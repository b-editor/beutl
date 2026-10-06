using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Beutl.Controls.PropertyEditors;

internal static class ColorPickerParts
{
    public static HsvColor GetHsvColor(object? sender, ColorChangedEventArgs args)
    {
        HsvColor color = args.NewColor.ToHsv();
        if (sender is ColorSpectrum spectrum)
        {
            color = spectrum.HsvColor;
        }
        else if (sender is ColorPreviewer previewer)
        {
            color = previewer.HsvColor;
        }

        return color;
    }
}
