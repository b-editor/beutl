using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    public static void AddConfirmHandlers(
        Interactive target,
        EventHandler<PointerPressedEventArgs> pressed,
        EventHandler<PointerReleasedEventArgs> released)
    {
        target.AddHandler(InputElement.PointerPressedEvent, pressed, handledEventsToo: true);
        target.AddHandler(InputElement.PointerReleasedEvent, released, handledEventsToo: true);
    }

    public static void RemoveConfirmHandlers(
        Interactive target,
        EventHandler<PointerPressedEventArgs> pressed,
        EventHandler<PointerReleasedEventArgs> released)
    {
        target.RemoveHandler(InputElement.PointerPressedEvent, pressed);
        target.RemoveHandler(InputElement.PointerReleasedEvent, released);
    }
}
