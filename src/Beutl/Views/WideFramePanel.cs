using Avalonia;
using Avalonia.Controls;

namespace Beutl.Views;

// Gives its children a 16:9 frame as wide as the panel is allowed to be, so a row of frames lines up whatever
// shape each picture has.
public sealed class WideFramePanel : Panel
{
    private const double AspectRatio = 16d / 9d;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
        var frame = new Size(width, width / AspectRatio);
        foreach (Control child in Children)
        {
            child.Measure(frame);
        }

        return frame;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (Control child in Children)
        {
            child.Arrange(new Rect(finalSize));
        }

        return finalSize;
    }
}
