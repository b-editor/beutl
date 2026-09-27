using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public sealed class GraphEditorSelectionAdorner : Control
{
    public GraphEditorSelectionAdorner() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public Rect? Selection { get; set; }
    public Rect? Marquee { get; set; }
    public Point? Anchor { get; set; }

    internal static Point HandlePoint(Rect r, int handle) => handle switch
    {
        0 => r.TopLeft,
        1 => new Point(r.Center.X, r.Top),
        2 => r.TopRight,
        3 => new Point(r.Right, r.Center.Y),
        4 => r.BottomRight,
        5 => new Point(r.Center.X, r.Bottom),
        6 => r.BottomLeft,
        7 => new Point(r.Left, r.Center.Y),
        _ => r.Center
    };

    internal int HitHandle(Point point)
    {
        if (Selection is not { } rect) return -1;
        for (int i = 8; i >= 0; i--)
        {
            Point handle = i == 8 ? Anchor ?? rect.Center : HandlePoint(rect, i);
            if (new Rect(handle - new Point(5, 5), new Size(10, 10)).Contains(point)) return i;
        }
        return -1;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var brush = this.TryFindResource("GraphEditorKeyFrameBrush", ActualThemeVariant, out var resource)
            ? resource as IBrush ?? Brushes.Orange : Brushes.Orange;
        var pen = new Pen(brush, 1);
        if (Marquee is { } marquee)
        {
            using (context.PushOpacity(0.15)) context.DrawRectangle(brush, null, marquee);
            context.DrawRectangle(null, pen, marquee);
        }
        if (Selection is not { } rectangle) return;
        context.DrawRectangle(null, new Pen(brush, 1, DashStyle.Dash), rectangle);
        for (int i = 0; i < 8; i++)
            context.DrawRectangle(brush, null, new Rect(HandlePoint(rectangle, i) - new Point(3, 3), new Size(6, 6)));
        Point anchor = Anchor ?? rectangle.Center;
        context.DrawEllipse(null, pen, anchor, 4, 4);
        context.DrawLine(pen, anchor - new Point(6, 0), anchor + new Point(6, 0));
        context.DrawLine(pen, anchor - new Point(0, 6), anchor + new Point(0, 6));
    }
}
