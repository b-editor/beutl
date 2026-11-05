using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Beutl.Controls;

namespace Beutl.Editor.Components.PropertyEditors.Views;

// What a list item shows when open. While its handle is here, in place of a row for the item's name, the handle sits in
// the gutter beside the label of the first row, and the line runs from under the handle to the last row. A single row
// has no line. Without the handle, the line runs along everything here.
public sealed class ListItemRowsPanel : Panel
{
    private const double RowTolerance = 0.5;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var bounds = new Rect(finalSize);
        ToggleButton? handle = Children.OfType<ToggleButton>().FirstOrDefault(c => c.IsVisible);
        TreeLineDecorator? line = Children.OfType<TreeLineDecorator>().FirstOrDefault();
        foreach (Control child in Children)
        {
            if (child != handle && child != line)
                child.Arrange(bounds);
        }

        if (handle != null)
        {
            // The rows are in place by now, so their label says where the handle goes.
            PropertiesEditor? rows = Children.OfType<PropertiesEditor>().FirstOrDefault();
            Control? firstRow = rows?.FirstRow;
            Size size = handle.DesiredSize;
            double top = Math.Max(0, (firstRow != null ? LabelCenter(firstRow) : 0) - size.Height / 2);
            handle.Arrange(new Rect(0, top, size.Width, size.Height));

            double lineTop = top + size.Height;
            bool hasMoreRows = firstRow != null && firstRow.Bounds.Bottom < rows!.Bounds.Height - RowTolerance;
            bounds = new Rect(0, lineTop, finalSize.Width, hasMoreRows ? Math.Max(0, finalSize.Height - lineTop) : 0);
        }

        line?.Arrange(bounds);
        return finalSize;
    }

    // A row that stacks its label over its value has the label above the row's center.
    private double LabelCenter(Control row)
    {
        TextBlock? label = row.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text));
        Control center = label ?? row;
        return center.TranslatePoint(new Point(0, center.Bounds.Height / 2), this)?.Y ?? 0;
    }
}
