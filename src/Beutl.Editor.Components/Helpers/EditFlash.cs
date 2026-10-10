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

    public static async void Run(Control target)
    {
        if (AdornerLayer.GetAdornerLayer(target) is not { } layer)
            return;

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
