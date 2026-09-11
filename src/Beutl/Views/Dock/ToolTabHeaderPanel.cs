using Avalonia;
using Avalonia.Controls;

namespace Beutl.Views.Dock;

/// <summary>
/// Lays out a tool dock header as [tab strip][add button][free space]. The add button is
/// measured first so it keeps its width even when the tabs overflow; the strip is then
/// measured with the remaining width so it clips instead of pushing the button out of view.
/// </summary>
public sealed class ToolTabHeaderPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        (Control? strip, Control? addButton, Control? freeSpace) = GetSlots();

        double buttonWidth = 0;
        if (addButton is not null)
        {
            addButton.Measure(availableSize);
            buttonWidth = addButton.DesiredSize.Width;
        }

        double stripWidth = 0;
        if (strip is not null)
        {
            strip.Measure(new Size(Math.Max(0, availableSize.Width - buttonWidth), availableSize.Height));
            stripWidth = strip.DesiredSize.Width;
        }

        freeSpace?.Measure(new Size(Math.Max(0, availableSize.Width - stripWidth - buttonWidth), availableSize.Height));

        double height = 0;
        foreach (Control child in Children)
        {
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(stripWidth + buttonWidth, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        (Control? strip, Control? addButton, Control? freeSpace) = GetSlots();

        double x = 0;
        if (strip is not null)
        {
            double width = Math.Min(strip.DesiredSize.Width, finalSize.Width);
            strip.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        if (addButton is not null)
        {
            double width = Math.Min(addButton.DesiredSize.Width, Math.Max(0, finalSize.Width - x));
            addButton.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        freeSpace?.Arrange(new Rect(x, 0, Math.Max(0, finalSize.Width - x), finalSize.Height));

        return finalSize;
    }

    private (Control? Strip, Control? AddButton, Control? FreeSpace) GetSlots()
    {
        return (
            Children.Count > 0 ? Children[0] : null,
            Children.Count > 1 ? Children[1] : null,
            Children.Count > 2 ? Children[2] : null);
    }
}
