using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace BeUtl.Views;

public sealed class TimelineBackground : TemplatedControl
{
    public static readonly StyledProperty<double> ItemHeightProperty = AvaloniaProperty.Register<TimelineBackground, double>(nameof(ItemHeight), 25);

    static TimelineBackground()
    {
        AffectsRender<TimelineBackground>(ItemHeightProperty);
    }

    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;
        double itemHeight = ItemHeight;
        var pen = new Pen()
        {
            Brush = BorderBrush,
            Thickness = BorderThickness.Top + BorderThickness.Bottom
        };
        for (double y = 0; y < height; y += itemHeight)
        {
            context.DrawLine(pen, new Point(0, y), new Point(width, y));
        }
    }
}
