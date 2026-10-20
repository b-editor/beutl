using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

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

// A press on one of a picker's parts, which are Avalonia's color controls. Those keep their pressed state when they lose
// the pointer capture, so the picker confirms the press then and drops the part's pointer moves until it is pressed again.
internal sealed class ColorPartPress
{
    private readonly Action _begin;
    private readonly Action _confirm;
    private readonly HashSet<Visual> _stale = [];
    private Interactive? _pressed;
    private Interactive? _releasing;

    public ColorPartPress(Control picker, Action begin, Action confirm)
    {
        _begin = begin;
        _confirm = confirm;
        picker.AddHandler(InputElement.PointerMovedEvent, OnPickerPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        picker.AddHandler(InputElement.PointerReleasedEvent, OnPickerPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public bool IsPressed => _pressed is not null;

    public void Add(Interactive part)
    {
        // Tunnel, so the press is known before the part sets the color under the pointer.
        part.AddHandler(InputElement.PointerPressedEvent, OnPartPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        part.AddHandler(InputElement.PointerReleasedEvent, OnPartReleased, handledEventsToo: true);
        part.AddHandler(InputElement.PointerCaptureLostEvent, OnPartCaptureLost);
    }

    public void Remove(Interactive part)
    {
        part.RemoveHandler(InputElement.PointerPressedEvent, OnPartPressed);
        part.RemoveHandler(InputElement.PointerReleasedEvent, OnPartReleased);
        part.RemoveHandler(InputElement.PointerCaptureLostEvent, OnPartCaptureLost);
    }

    // Drops the press in progress without confirming it, for a host that ended the gesture itself.
    public void Stop()
    {
        if (_pressed is Visual part)
            _stale.Add(part);
        _pressed = null;
    }

    private void OnPartPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Interactive part) return;

        _pressed = part;
        if (part is Visual visual)
            _stale.Remove(visual);
        _begin();
    }

    // Runs before the part handles the release, so the capture loss the part causes then does not count as lost.
    private void OnPickerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _releasing = _pressed;
        _pressed = null;
    }

    private void OnPartReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is null || sender != _releasing) return;

        _releasing = null;
        _confirm();
    }

    private void OnPartCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sender is null || sender != _pressed) return;

        Stop();
        _confirm();
    }

    private void OnPickerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_stale.Count > 0 && e.Source is Visual source && _stale.Any(part => part == source || part.IsVisualAncestorOf(source)))
            e.Handled = true;
    }
}
