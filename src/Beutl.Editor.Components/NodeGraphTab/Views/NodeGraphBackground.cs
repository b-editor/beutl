using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Media;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

public sealed class NodeGraphBackground : ZoomBorderBoundControl
{
    public static readonly StyledProperty<IBrush?> BorderBrushProperty
        = Border.BorderBrushProperty.AddOwner<NodeGraphBackground>();

    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        ZoomBorder zoomBorder = ZoomBorder;
        double width = Bounds.Width / zoomBorder.ZoomX;
        double height = Bounds.Height / zoomBorder.ZoomY;

        const double DotSize = 15;

        int hsplit = (int)Math.Ceiling(width / DotSize) + 1;
        int vsplit = (int)Math.Ceiling(height / DotSize) + 1;

        double offsetX = zoomBorder.OffsetX / zoomBorder.ZoomX;
        double offsetY = zoomBorder.OffsetY / zoomBorder.ZoomY;
        double amariX = offsetX % DotSize;
        double amariY = offsetY % DotSize;

        using (context.PushTransform(Matrix.CreateTranslation(amariX, amariY) * Matrix.CreateScale(zoomBorder.ZoomX, zoomBorder.ZoomY)))
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
