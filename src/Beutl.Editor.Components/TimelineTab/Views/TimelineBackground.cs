using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class TimelineBackground : TemplatedControl
{
    static TimelineBackground()
    {
        AffectsRender<TimelineBackground>(ItemHeightProperty, BorderBrushProperty);
    }

    [GeneratedStyledProperty(DefaultValue = 25)]
    public partial double ItemHeight { get; set; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;
        double itemHeight = ItemHeight;
        var pen = new Pen()
        {
            Brush = BorderBrush,
            Thickness = 0.2,
        };
        for (double y = 0; y < height; y += itemHeight)
        {
            context.DrawLine(pen, new Point(0, y), new Point(width, y));
        }
    }
}
