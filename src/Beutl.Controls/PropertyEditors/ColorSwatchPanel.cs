using Avalonia;
using Avalonia.Controls;

namespace Beutl.Controls.PropertyEditors;

// Lays its children out as square cells, Columns to a row, each cell the width divided by Columns, so that
// palettes of any length line their swatches up under each other.
internal sealed class ColorSwatchPanel : Panel
{
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<ColorSwatchPanel, int>(nameof(Columns), 10, validate: v => v > 0);

    // The cell size when the width is not bounded.
    private const double UnboundedCellSize = 24;

    static ColorSwatchPanel()
    {
        AffectsMeasure<ColorSwatchPanel>(ColumnsProperty);
    }

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double cell = GetCellSize(availableSize.Width);
        var cellSize = new Size(cell, cell);
        foreach (Control child in Children)
        {
            child.Measure(cellSize);
        }

        // A short last row still takes the whole width, so that arranging keeps the cells this size.
        int rows = (Children.Count + Columns - 1) / Columns;
        return new Size(cell * Columns, cell * rows);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double cell = GetCellSize(finalSize.Width);
        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Arrange(new Rect(i % Columns * cell, i / Columns * cell, cell, cell));
        }

        return finalSize;
    }

    private double GetCellSize(double width)
    {
        return double.IsInfinity(width) ? UnboundedCellSize : width / Columns;
    }
}
