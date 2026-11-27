using Avalonia;
using Avalonia.Controls;

namespace Beutl.Controls;

/// <summary>
/// Keeps the first child at the leading edge and right-aligns the remaining controls,
/// wrapping them onto additional rows when the bar is narrow.
/// </summary>
public sealed partial class ToolTabBarPanel : Panel
{
    static ToolTabBarPanel()
    {
        AffectsMeasure<ToolTabBarPanel>(ItemSpacingProperty, LineSpacingProperty);
    }

    [GeneratedStyledProperty(DefaultValue = 4)]
    public partial double ItemSpacing { get; set; }

    [GeneratedStyledProperty(DefaultValue = 4)]
    public partial double LineSpacing { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (Control child in Children)
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));

        List<Row> rows = BuildRows(availableSize.Width);
        return rows.Count == 0 ? default : new Size(
            rows.Max(row => row.Width),
            rows.Sum(row => row.Height) + LineSpacing * (rows.Count - 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        foreach (Row row in BuildRows(finalSize.Width))
        {
            double x = Math.Max(0, finalSize.Width - row.Width);
            foreach (Control child in row.Controls)
            {
                double left = ReferenceEquals(child, Children[0]) ? 0 : x;
                child.Arrange(new Rect(left, y, child.DesiredSize.Width, row.Height));
                x += child.DesiredSize.Width + ItemSpacing;
            }
            y += row.Height + LineSpacing;
        }
        return finalSize;
    }

    private List<Row> BuildRows(double width)
    {
        var rows = new List<Row>();
        var row = new Row();
        foreach (Control child in Children)
        {
            if (!child.IsVisible) continue;
            double gap = row.Controls.Count > 0 ? ItemSpacing : 0;
            if (row.Controls.Count > 0 && row.Width + gap + child.DesiredSize.Width > width)
            {
                rows.Add(row);
                row = new Row();
                gap = 0;
            }
            row.Controls.Add(child);
            row.Width += gap + child.DesiredSize.Width;
            row.Height = Math.Max(row.Height, child.DesiredSize.Height);
        }
        if (row.Controls.Count > 0) rows.Add(row);
        return rows;
    }

    private sealed class Row
    {
        public List<Control> Controls { get; } = [];
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
