using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Media;

namespace Beutl.Views.NodeTree;

public sealed class NodeTreeBackground : Control
{
    public static readonly DirectProperty<NodeTreeBackground, ZoomBorder> ZoomBorderProperty
        = AvaloniaProperty.RegisterDirect<NodeTreeBackground, ZoomBorder>(
            nameof(ZoomBorder),
            o => o.ZoomBorder,
            (o, v) => o.ZoomBorder = v);

    public static readonly StyledProperty<IBrush?> BorderBrushProperty
        = Border.BorderBrushProperty.AddOwner<NodeTreeBackground>();

    private ZoomBorder _zoomBorder = null!;

    public ZoomBorder ZoomBorder
    {
        get => _zoomBorder;
        set => SetAndRaise(ZoomBorderProperty, ref _zoomBorder, value);
    }

    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
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

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width / _zoomBorder.ZoomX;
        double height = Bounds.Height / _zoomBorder.ZoomY;

        const double DotSize = 15;

        int hsplit = (int)Math.Ceiling(width / DotSize) + 1;
        int vsplit = (int)Math.Ceiling(height / DotSize) + 1;

        double offsetX = _zoomBorder.OffsetX;
        double offsetY = _zoomBorder.OffsetY;
        double amariX = offsetX % DotSize;
        double amariY = offsetY % DotSize;

        using (context.PushPreTransform(Matrix.CreateTranslation(amariX, amariY) * Matrix.CreateScale(_zoomBorder.ZoomX, _zoomBorder.ZoomY)))
        {
            IBrush brush = BorderBrush ?? Brushes.Gray;
            for (int i = -1; i < hsplit; i++)
            {
                double x = i * DotSize;
                context.FillRectangle(brush, new Rect(x, 0, 1, height).Inflate(new Thickness(0, DotSize)));
            }

            for (int i = -1; i < vsplit; i++)
            {
                double y = i * DotSize;
                context.FillRectangle(brush, new Rect(0, y, width, 1).Inflate(new Thickness(DotSize, 0)));
            }
        }
    }
}
