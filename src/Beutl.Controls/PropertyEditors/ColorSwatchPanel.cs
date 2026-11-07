using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Beutl.Controls.PropertyEditors;

// Lays its children out as square cells, Columns to a row, each cell the width divided by Columns, so that
// palettes of any length line their swatches up under each other. Tab stops once in it, at the swatch focused
// last, and the arrow keys move between its swatches.
internal sealed class ColorSwatchPanel : Panel
{
    private const int Columns = 10;

    // The cell size when the width is not bounded.
    private const double UnboundedCellSize = 24;

    static ColorSwatchPanel()
    {
        KeyboardNavigation.TabNavigationProperty.OverrideDefaultValue<ColorSwatchPanel>(KeyboardNavigationMode.Once);
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

    // Left and Right step through the swatches in order, Up and Down a row at a time. At an edge the key does
    // nothing, rather than scroll the palettes.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || e.Source is not Control source)
        {
            return;
        }

        int index = Children.IndexOf(source);
        int? target = e.Key switch
        {
            Key.Left => index - 1,
            Key.Right => index + 1,
            Key.Up => index - Columns,
            Key.Down => index + Columns,
            _ => null
        };
        if (index < 0 || target is not { } next)
        {
            return;
        }

        e.Handled = true;
        if (next >= 0 && next < Children.Count)
        {
            Children[next].Focus(NavigationMethod.Directional);
        }
    }

    private double GetCellSize(double width)
    {
        return double.IsInfinity(width) ? UnboundedCellSize : width / Columns;
    }
}
