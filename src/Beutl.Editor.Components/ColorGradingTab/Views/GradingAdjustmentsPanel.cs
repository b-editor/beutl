using Avalonia;
using Avalonia.Controls;

namespace Beutl.Editor.Components.ColorGradingTab.Views;

// Puts the groups of adjustments in rows of up to Columns equal cells, each as high as its tallest group. When a cell
// would be narrower than MinColumnWidth it uses fewer columns, down to one, which stacks the groups.
public sealed class GradingAdjustmentsPanel : Panel
{
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<GradingAdjustmentsPanel, int>(nameof(Columns), 3);

    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<GradingAdjustmentsPanel, double>(nameof(MinColumnWidth), 280);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<GradingAdjustmentsPanel, double>(nameof(ColumnSpacing), 24);

    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<GradingAdjustmentsPanel, double>(nameof(RowSpacing), 12);

    static GradingAdjustmentsPanel()
    {
        AffectsMeasure<GradingAdjustmentsPanel>(ColumnsProperty, MinColumnWidthProperty, ColumnSpacingProperty, RowSpacingProperty);
    }

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    // The columns of the last layout, for tests.
    internal int UsedColumns { get; private set; } = 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        Control[] items = [.. Children.Where(child => child.IsVisible)];
        int columns = ColumnsFor(availableSize.Width);
        double cellWidth = CellWidth(availableSize.Width, columns);
        double height = 0;
        double widest = 0;
        for (int row = 0; row * columns < items.Length; row++)
        {
            double rowHeight = 0;
            foreach (Control item in items.Skip(row * columns).Take(columns))
            {
                item.Measure(new Size(cellWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, item.DesiredSize.Height);
                widest = Math.Max(widest, item.DesiredSize.Width);
            }

            height += rowHeight + (row > 0 ? RowSpacing : 0);
        }

        UsedColumns = columns;
        double width = double.IsFinite(availableSize.Width)
            ? availableSize.Width
            : (columns * widest) + ((columns - 1) * ColumnSpacing);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Control[] items = [.. Children.Where(child => child.IsVisible)];
        int columns = ColumnsFor(finalSize.Width);
        double cellWidth = CellWidth(finalSize.Width, columns);
        double y = 0;
        for (int row = 0; row * columns < items.Length; row++)
        {
            Control[] cells = [.. items.Skip(row * columns).Take(columns)];
            double rowHeight = cells.Max(item => item.DesiredSize.Height);
            for (int column = 0; column < cells.Length; column++)
            {
                double x = column * (cellWidth + ColumnSpacing);
                cells[column].Arrange(new Rect(x, y, cellWidth, rowHeight));
            }

            y += rowHeight + RowSpacing;
        }

        return finalSize;
    }

    private int ColumnsFor(double width)
    {
        int columns = Math.Max(Columns, 1);
        if (!double.IsFinite(width))
            return columns;

        while (columns > 1 && CellWidth(width, columns) < MinColumnWidth)
            columns--;

        return columns;
    }

    private double CellWidth(double width, int columns)
        => double.IsFinite(width) ? Math.Max((width - (ColumnSpacing * (columns - 1))) / columns, 0) : double.PositiveInfinity;
}
