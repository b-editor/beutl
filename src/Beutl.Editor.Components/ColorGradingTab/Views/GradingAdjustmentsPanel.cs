using Avalonia;
using Avalonia.Controls;

namespace Beutl.Editor.Components.ColorGradingTab.Views;

// Puts the groups of adjustments in rows of up to Columns equal cells, each as high as its tallest group. When a cell
// would be narrower than MinColumnWidth it uses fewer columns, down to one, which stacks the groups; with no width to
// share, as in a column as wide as what it holds, the groups stack too.
public sealed class GradingAdjustmentsPanel : Panel
{
    private const int Columns = 3;
    private const double MinColumnWidth = 280;
    private const double ColumnSpacing = 24;
    private const double RowSpacing = 12;

    // The columns of the last layout, for tests.
    internal int UsedColumns { get; private set; } = 1;

    // The height the groups take at a width, from their heights in the last layout, which no width changes since each
    // of their rows keeps one line. The tab uses it to know whether they fit above the wheels before putting them there.
    internal double HeightAt(double width) => HeightOf(VisibleItems(), ColumnsFor(width));

    protected override Size MeasureOverride(Size availableSize)
    {
        Control[] items = VisibleItems();
        int columns = ColumnsFor(availableSize.Width);
        double cellWidth = CellWidth(availableSize.Width, columns);
        foreach (Control item in items)
            item.Measure(new Size(cellWidth, double.PositiveInfinity));

        UsedColumns = columns;
        double width = double.IsFinite(availableSize.Width)
            ? availableSize.Width
            : (columns * items.Select(item => item.DesiredSize.Width).DefaultIfEmpty().Max()) + ((columns - 1) * ColumnSpacing);
        return new Size(width, HeightOf(items, columns));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = ColumnsFor(finalSize.Width);
        double cellWidth = CellWidth(finalSize.Width, columns);
        double y = 0;
        foreach (Control[] row in VisibleItems().Chunk(columns))
        {
            double rowHeight = RowHeight(row);
            for (int column = 0; column < row.Length; column++)
                row[column].Arrange(new Rect(column * (cellWidth + ColumnSpacing), y, cellWidth, rowHeight));

            y += rowHeight + RowSpacing;
        }

        return finalSize;
    }

    private static double RowHeight(Control[] row) => row.Max(item => item.DesiredSize.Height);

    private Control[] VisibleItems() => [.. Children.Where(child => child.IsVisible)];

    private double HeightOf(Control[] items, int columns)
    {
        Control[][] rows = [.. items.Chunk(columns)];
        return rows.Sum(RowHeight) + (Math.Max(rows.Length - 1, 0) * RowSpacing);
    }

    private int ColumnsFor(double width)
    {
        if (!double.IsFinite(width))
            return 1;

        int columns = Columns;

        while (columns > 1 && CellWidth(width, columns) < MinColumnWidth)
            columns--;

        return columns;
    }

    private double CellWidth(double width, int columns)
        => double.IsFinite(width) ? Math.Max((width - (ColumnSpacing * (columns - 1))) / columns, 0) : double.PositiveInfinity;
}
