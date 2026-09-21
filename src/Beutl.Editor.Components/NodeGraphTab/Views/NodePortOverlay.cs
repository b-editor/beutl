using Avalonia;
using Avalonia.Controls;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

// This must not be a Canvas: port drags find the graph Canvas to host their temporary wire.
// Keeping connectors outside the property editors also avoids their clipping and indentation.
public sealed class NodePortOverlay : Panel
{
    internal void SetPosition(Control port, Point position)
    {
        if (Canvas.GetLeft(port) == position.X && Canvas.GetTop(port) == position.Y) return;
        Canvas.SetLeft(port, position.X);
        Canvas.SetTop(port, position.Y);
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (Control child in Children) child.Measure(Size.Infinity);
        // Connector positions follow the rows; they must not keep a collapsed node tall.
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (Control child in Children)
        {
            double x = Canvas.GetLeft(child);
            double y = Canvas.GetTop(child);
            child.Arrange(new Rect(new Point(double.IsNaN(x) ? 0 : x, double.IsNaN(y) ? 0 : y), child.DesiredSize));
        }
        return finalSize;
    }
}
