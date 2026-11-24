using Avalonia;
using Avalonia.Controls;

namespace Beutl.Editor.Components.ColorGradingTab.Views;

internal sealed class GradingContentScrollViewer : ScrollViewer
{
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    protected override Size MeasureOverride(Size availableSize)
    {
        // The scroll content can grow beyond the viewport. Use the viewport height to choose its layout.
        ((GradingContentPanel)Content!).ViewportHeight = availableSize.Height;
        return base.MeasureOverride(availableSize);
    }
}
