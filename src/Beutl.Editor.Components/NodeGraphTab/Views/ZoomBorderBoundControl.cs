using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

// A node graph layer that redraws whenever its ZoomBorder pans or zooms.
public abstract class ZoomBorderBoundControl : Control
{
    public static readonly DirectProperty<ZoomBorderBoundControl, ZoomBorder> ZoomBorderProperty
        = AvaloniaProperty.RegisterDirect<ZoomBorderBoundControl, ZoomBorder>(
            nameof(ZoomBorder),
            o => o.ZoomBorder,
            (o, v) => o.ZoomBorder = v);

    private ZoomBorder _zoomBorder = null!;

    public ZoomBorder ZoomBorder
    {
        get => _zoomBorder;
        set => SetAndRaise(ZoomBorderProperty, ref _zoomBorder, value);
    }

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
