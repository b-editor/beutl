using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Beutl.Controls;

// Shows and hides an overlay (the command palette, the tab switcher) in place of binding its IsVisible: it fades in
// while its surface grows slightly into place. It hides at once, because the overlays empty their lists as they close
// and a fade would show the empty card.
public static class OverlayMotion
{
    private const double SurfaceStartScale = 0.97;

    public static readonly AttachedProperty<bool> IsOpenProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsOpen", typeof(OverlayMotion));

    // The card that grows into place; the whole overlay fades.
    public static readonly AttachedProperty<Visual?> SurfaceProperty =
        AvaloniaProperty.RegisterAttached<Control, Visual?>("Surface", typeof(OverlayMotion));

    static OverlayMotion()
    {
        IsOpenProperty.Changed.AddClassHandler<Control>((overlay, e) => OnIsOpenChanged(overlay, e.GetNewValue<bool>()));
    }

    public static bool GetIsOpen(Control control) => control.GetValue(IsOpenProperty);

    public static void SetIsOpen(Control control, bool value) => control.SetValue(IsOpenProperty, value);

    public static Visual? GetSurface(Control control) => control.GetValue(SurfaceProperty);

    public static void SetSurface(Control control, Visual? value) => control.SetValue(SurfaceProperty, value);

    private static void OnIsOpenChanged(Control overlay, bool open)
    {
        Visual? surface = GetSurface(overlay);
        if (open)
        {
            overlay.IsVisible = true;
            if (TopLevel.GetTopLevel(overlay) is null)
            {
                UiMotion.Cancel(overlay);
                overlay.Opacity = 1;
                return;
            }

            ScaleTransform? grow = surface is { RenderTransform: null or ScaleTransform }
                ? new ScaleTransform(SurfaceStartScale, SurfaceStartScale)
                : null;
            if (grow != null) surface!.RenderTransform = grow;
            overlay.Opacity = 0;

            UiMotion.Run(overlay, UiMotion.Normal, progress =>
            {
                overlay.Opacity = progress;
                if (grow != null && ReferenceEquals(surface!.RenderTransform, grow))
                {
                    double scale = SurfaceStartScale + (1 - SurfaceStartScale) * progress;
                    grow.ScaleX = scale;
                    grow.ScaleY = scale;
                }
            }, () =>
            {
                if (grow != null && ReferenceEquals(surface!.RenderTransform, grow))
                    surface.RenderTransform = null;
            });
        }
        else
        {
            UiMotion.Cancel(overlay);
            if (surface?.RenderTransform is ScaleTransform) surface.RenderTransform = null;
            overlay.IsVisible = false;
            overlay.Opacity = 1;
        }
    }
}
