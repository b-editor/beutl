using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;

namespace Beutl.Editor.Components.Helpers;

// Briefly outlines a control in its adorner layer, to point at an edit the user did not make
// themselves (for example one made by a live MCP agent).
public static class EditFlash
{
    private static readonly Avalonia.Animation.Animation s_pulse = new()
    {
        Duration = TimeSpan.FromSeconds(1.6),
        FillMode = FillMode.Forward,
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
            new KeyFrame { Cue = new Cue(0.12), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
            new KeyFrame { Cue = new Cue(0.65), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
        }
    };

    public static void Run(Control target)
    {
        if (AdornerLayer.GetAdornerLayer(target) is not { } layer
            || TopLevel.GetTopLevel(target) is not { } topLevel)
        {
            return;
        }

        Color color = target.TryFindResource("SystemAccentColor", target.ActualThemeVariant, out object? resource)
                      && resource is Color accent
            ? accent
            : Colors.DodgerBlue;
        var outline = new Border
        {
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(color, 0.2),
            CornerRadius = new CornerRadius(4),
            IsHitTestVisible = false,
            Opacity = 0
        };
        AdornerLayer.SetAdornedElement(outline, target);
        layer.Children.Add(outline);
        // Avalonia times an animation started mid-frame, or while a frame is still being composed, from that
        // frame's start, so after a slow layout (an edit reopening the property tab, say) much of the pulse would
        // be over before it showed. Start it on the next frame instead.
        topLevel.RequestAnimationFrame(_ => Pulse(layer, outline));
    }

    private static async void Pulse(AdornerLayer layer, Border outline)
    {
        try
        {
            await s_pulse.RunAsync(outline);
        }
        finally
        {
            layer.Children.Remove(outline);
        }
    }
}
