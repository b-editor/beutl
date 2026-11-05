using Avalonia;
using Avalonia.Controls;

namespace Beutl.Controls;

/// <summary>
/// Lays out the player's footer: the first child at the leading edge, the second centered in the whole width and
/// the third at the trailing edge. The second takes a row of its own when the three do not fit side by side, and the
/// first and the third take one each when those two do not fit side by side either.
/// </summary>
public sealed class PlayerFooterPanel : Panel
{
    private Size LeadingSize => Children.Count > 0 ? Children[0].DesiredSize : default;

    private Size CenterSize => Children.Count > 1 ? Children[1].DesiredSize : default;

    private Size TrailingSize => Children.Count > 2 ? Children[2].DesiredSize : default;

    // The center stays in the middle of the panel, so the wider side decides what fits.
    private double SingleRowWidth => (Math.Max(LeadingSize.Width, TrailingSize.Width) * 2) + CenterSize.Width;

    private double SidesWidth => LeadingSize.Width + TrailingSize.Width;

    private double SidesHeight => Math.Max(LeadingSize.Height, TrailingSize.Height);

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (Control child in Children)
            child.Measure(Size.Infinity);

        if (SingleRowWidth <= availableSize.Width)
            return new Size(SingleRowWidth, Math.Max(SidesHeight, CenterSize.Height));

        return SidesWidth <= availableSize.Width
            ? new Size(Math.Max(SidesWidth, CenterSize.Width), SidesHeight + CenterSize.Height)
            : new Size(
                Math.Max(Math.Max(LeadingSize.Width, TrailingSize.Width), CenterSize.Width),
                LeadingSize.Height + TrailingSize.Height + CenterSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        bool singleRow = SingleRowWidth <= finalSize.Width;
        bool sidesShareRow = SidesWidth <= finalSize.Width;
        double leadingHeight = singleRow ? finalSize.Height : sidesShareRow ? SidesHeight : LeadingSize.Height;
        double trailingHeight = singleRow ? finalSize.Height : sidesShareRow ? SidesHeight : TrailingSize.Height;
        double trailingY = sidesShareRow ? 0 : leadingHeight;
        double centerX = (finalSize.Width - CenterSize.Width) / 2;

        if (Children.Count > 0)
            Children[0].Arrange(new Rect(0, 0, LeadingSize.Width, leadingHeight));
        if (Children.Count > 1)
        {
            Children[1].Arrange(singleRow
                ? new Rect(centerX, 0, CenterSize.Width, finalSize.Height)
                : new Rect(centerX, trailingY + trailingHeight, CenterSize.Width, CenterSize.Height));
        }

        if (Children.Count > 2)
            Children[2].Arrange(new Rect(finalSize.Width - TrailingSize.Width, trailingY, TrailingSize.Width, trailingHeight));

        return finalSize;
    }
}
