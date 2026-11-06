using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Beutl.Controls;

namespace Beutl.Editor.Components.PropertyEditors.Views;

// What a list item shows when open. While its handle is here, in place of a row for the item's name, the handle sits in
// the gutter beside the label of the first row, and the line runs from under the handle to the last row. A single row
// with its value beside its label has no line. Without the handle, the line runs along everything here.
public sealed class ListItemRowsPanel : Panel
{
    // Layout rounding can move a centered label by a pixel.
    private const double LabelTolerance = 2;
    private const double LayoutTolerance = .01;
    // The space above the rows that lets the handle center on a label nearer their top than half the handle, as the
    // label of a row that stacks its value under it is.
    private double _rowsTop;

    protected override Size MeasureOverride(Size availableSize)
    {
        Size size = base.MeasureOverride(availableSize.Deflate(new Thickness(0, _rowsTop, 0, 0)));
        return size.WithHeight(size.Height + _rowsTop);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        ToggleButton? handle = Children.OfType<ToggleButton>().FirstOrDefault(c => c.IsVisible);
        TreeLineDecorator? line = Children.OfType<TreeLineDecorator>().FirstOrDefault();
        double rowsTop = handle != null ? _rowsTop : 0;
        var bounds = new Rect(0, rowsTop, finalSize.Width, Math.Max(0, finalSize.Height - rowsTop));
        foreach (Control child in Children)
        {
            if (child != handle && child != line)
                child.Arrange(bounds);
        }

        bounds = new Rect(finalSize);
        double neededTop = 0;
        if (handle != null)
        {
            // The rows are in place by now, so their label says where the handle goes.
            PropertiesEditor? rows = Children.OfType<PropertiesEditor>().FirstOrDefault();
            Control? firstItem = rows?.FirstItem;
            Size size = handle.DesiredSize;
            double label = 0;
            if (firstItem != null)
            {
                // Where the label is in the rows does not depend on where the rows are.
                double inRows = LabelCenter(firstItem, rows!);
                neededTop = Math.Max(0, size.Height / 2 - inRows);
                label = rows!.Bounds.Top + inRows;
            }

            double top = Math.Max(0, label - size.Height / 2);
            handle.Arrange(new Rect(0, top, size.Width, size.Height));

            // A row with its value beside its label centers the label, so it ends as far below the label as it starts
            // above it. Below that come the rows after the first, the second of a pair kept apart, or a value stacked
            // under its label, and the line runs beside them.
            double lineTop = top + size.Height;
            bool goesOn = firstItem?.TranslatePoint(default, this) is { } itemTop
                && rows!.Bounds.Bottom - label > label - itemTop.Y + LabelTolerance;
            bounds = new Rect(0, lineTop, finalSize.Width, goesOn ? Math.Max(0, finalSize.Height - lineTop) : 0);
        }

        line?.Arrange(bounds);
        // Only rows in place say where their label is, so the space above them follows in the next layout pass.
        if (Math.Abs(neededTop - _rowsTop) > LayoutTolerance)
        {
            _rowsTop = neededTop;
            InvalidateMeasure();
        }

        return finalSize;
    }

    // A row that stacks its label over its value has the label above the row's center.
    private static double LabelCenter(Control row, Visual relativeTo)
    {
        TextBlock? label = row.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text));
        Control center = label ?? row;
        return center.TranslatePoint(new Point(0, center.Bounds.Height / 2), relativeTo)?.Y ?? 0;
    }
}
