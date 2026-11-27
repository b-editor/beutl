using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

// A node graph layer that redraws whenever its ZoomBorder pans or zooms.
public abstract partial class ZoomBorderBoundControl : Control
{
    [GeneratedDirectProperty]
    public partial ZoomBorder ZoomBorder { get; set; } = null!;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ZoomBorderProperty)
        {
            if (change.OldValue is ZoomBorder oldValue)
            {
                oldValue.ZoomChanged -= OnZoomChanged;
            }

            if (change.NewValue is ZoomBorder newValue)
            {
                newValue.ZoomChanged += OnZoomChanged;
            }
        }
    }

    private void OnZoomChanged(object sender, ZoomChangedEventArgs e)
    {
        InvalidateVisual();
    }
}
