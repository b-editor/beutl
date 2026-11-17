using Avalonia;
using Avalonia.Controls;
using Beutl.Controls.PropertyEditors;
using static Beutl.Editor.Components.ColorGradingTab.Views.ColorGradingTabView;

namespace Beutl.Editor.Components.ColorGradingTab.Views;

internal sealed class GradingContentPanel : Panel
{
    private const double AboveGap = 12;
    private const double BesideGap = 16;
    private const double ColumnWidth = 330;
    private const double BesideMinWidth = 860;
    private const double BesideAspectRatio = 2.4;
    private const double MinWheelPad = 96;
    private const double MinScrollerHeight = 72;

    // The tab supplies the adjustment scroller, its two alternative wheel sets and the tonal range row.
    private ScrollViewer Scroller => (ScrollViewer)Children[0];
    private ColorWheelsPanel? Wheels => Children.OfType<ColorWheelsPanel>().FirstOrDefault(w => w.IsVisible);
    private Control RangeRow => Children[^1];

    internal Control[] SliderRows { get; set; } = [];
    internal AdjustmentsPlacement CurrentPlacement { get; private set; }
    internal AdjustmentsPlacement? Placement
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = availableSize.Width;
        double height = availableSize.Height;
        ColorWheelsPanel? wheels = Wheels;
        var placement = Placement ?? (width >= BesideMinWidth && width >= height * BesideAspectRatio
            ? AdjustmentsPlacement.Beside : AdjustmentsPlacement.Above);

        if (Scroller.IsVisible && placement == AdjustmentsPlacement.Above)
        {
            SetCompact(false);
            RangeRow.Measure(new Size(width, double.PositiveInfinity));
            double reserve = (wheels?.MeasureHeightForPad(width, MinWheelPad) ?? 0) + RangeHeight + AboveGap;
            double aboveHeight = Math.Max(MinScrollerHeight, height - reserve);
            if (Placement is null && width >= height)
            {
                Scroller.Measure(new Size(width, double.PositiveInfinity));
                if (Scroller.DesiredSize.Height > aboveHeight)
                    placement = AdjustmentsPlacement.Compact;
            }
            if (placement == AdjustmentsPlacement.Above)
                Scroller.Measure(new Size(width, Math.Min(height, aboveHeight)));
        }

        CurrentPlacement = placement;
        if (placement != AdjustmentsPlacement.Above)
        {
            SetCompact(placement == AdjustmentsPlacement.Compact);
            double column = placement == AdjustmentsPlacement.Beside ? Math.Min(width, ColumnWidth) : double.PositiveInfinity;
            Scroller.Measure(new Size(column, height));
        }

        bool above = placement == AdjustmentsPlacement.Above;
        double side = above ? 0 : ScrollerExtent(width);
        double top = above ? ScrollerExtent(height) : 0;
        double wheelWidth = Math.Max(0, width - side);
        RangeRow.Measure(new Size(wheelWidth, double.PositiveInfinity));
        wheels?.Measure(new Size(wheelWidth, Math.Max(0, height - top - RangeHeight)));
        Size wheelSize = wheels?.DesiredSize ?? default;
        double contentWidth = Math.Max(wheelSize.Width, RangeRow.DesiredSize.Width);
        double contentHeight = wheelSize.Height + RangeHeight;
        Size scrollerSize = Scroller.IsVisible ? Scroller.DesiredSize : default;
        return above
            ? new Size(Math.Max(contentWidth, scrollerSize.Width), top + contentHeight)
            : new Size(side + contentWidth, Math.Max(scrollerSize.Height, contentHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        bool above = CurrentPlacement == AdjustmentsPlacement.Above;
        double extent = ScrollerExtent(above ? finalSize.Height : finalSize.Width);
        double width = above ? finalSize.Width : finalSize.Width - extent;
        double height = above ? finalSize.Height - extent : finalSize.Height;
        if (Scroller.IsVisible)
        {
            Scroller.Arrange(above
                ? new Rect(0, 0, width, Math.Max(0, extent - AboveGap))
                : new Rect(width + BesideGap, 0, Math.Max(0, extent - BesideGap), height));
        }

        ColorWheelsPanel? wheels = Wheels;
        double wheelHeight = Math.Min(wheels?.DesiredSize.Height ?? 0, Math.Max(0, height - RangeHeight));
        double y = (above ? extent : 0) + Math.Max(0, (height - wheelHeight - RangeHeight) / 2);
        wheels?.Arrange(new Rect(0, y, width, wheelHeight));
        if (RangeRow.IsVisible)
            RangeRow.Arrange(new Rect(0, y + wheelHeight + AboveGap, width, RangeRow.DesiredSize.Height));
        return finalSize;
    }

    private double RangeHeight => RangeRow.IsVisible ? RangeRow.DesiredSize.Height + AboveGap : 0;

    private double ScrollerExtent(double limit)
    {
        if (!Scroller.IsVisible) return 0;
        return Math.Min(limit, CurrentPlacement == AdjustmentsPlacement.Above
            ? Scroller.DesiredSize.Height + AboveGap : Scroller.DesiredSize.Width + BesideGap);
    }

    private void SetCompact(bool compact)
    {
        foreach (Control row in SliderRows)
            row.Classes.Set("compact", compact);
    }
}
