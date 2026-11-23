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

    // The tab supplies separate scrolling areas for adjustments and wheels.
    private ScrollViewer Scroller => (ScrollViewer)Children[0];
    private ScrollViewer WheelScroller => (ScrollViewer)Children[1];
    private GradingWheelsContentPanel WheelContent => (GradingWheelsContentPanel)WheelScroller.Content!;

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
        var placement = Placement ?? (width >= BesideMinWidth && width >= height * BesideAspectRatio
            ? AdjustmentsPlacement.Beside : AdjustmentsPlacement.Above);

        if (Scroller.IsVisible && placement == AdjustmentsPlacement.Above)
        {
            SetCompact(false);
            double reserve = WheelContent.MeasureHeightForPad(width, MinWheelPad) + AboveGap;
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
        WheelContent.ViewportHeight = Math.Max(0, height - top);
        WheelScroller.Measure(new Size(wheelWidth, WheelContent.ViewportHeight));
        Size content = WheelScroller.DesiredSize;
        Size scrollerSize = Scroller.IsVisible ? Scroller.DesiredSize : default;
        return above
            ? new Size(Math.Max(content.Width, scrollerSize.Width), top + content.Height)
            : new Size(side + content.Width, Math.Max(scrollerSize.Height, content.Height));
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

        WheelScroller.Arrange(new Rect(0, above ? extent : 0, width, height));
        return finalSize;
    }

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

internal sealed class GradingWheelsContentPanel : Panel
{
    private const double RangeGap = 12;

    private ColorWheelsPanel? Wheels => Children.OfType<ColorWheelsPanel>().FirstOrDefault(wheel => wheel.IsVisible);
    private Control RangeRow => Children[^1];

    // The ScrollViewer measures content with unlimited height. The finite viewport still decides the wheel size.
    internal double ViewportHeight
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            InvalidateMeasure();
        }
    }

    private double RangeHeight => RangeRow.IsVisible ? RangeRow.DesiredSize.Height + RangeGap : 0;

    internal double MeasureHeightForPad(double width, double pad)
    {
        RangeRow.Measure(new Size(width, double.PositiveInfinity));
        return (Wheels?.MeasureHeightForPad(width, pad) ?? 0) + RangeHeight;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        ColorWheelsPanel? wheels = Wheels;
        RangeRow.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        if (wheels != null)
        {
            wheels.ViewportHeight = Math.Max(0, ViewportHeight - RangeHeight);
            wheels.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        }
        Size wheelSize = wheels?.DesiredSize ?? default;
        // When the minimum layout still overflows, keep its full size so all rows can be scrolled into view.
        return new Size(Math.Max(wheelSize.Width, RangeRow.DesiredSize.Width), wheelSize.Height + RangeHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        ColorWheelsPanel? wheels = Wheels;
        double wheelHeight = wheels?.DesiredSize.Height ?? 0;
        double y = Math.Max(0, (finalSize.Height - wheelHeight - RangeHeight) / 2);
        wheels?.Arrange(new Rect(0, y, finalSize.Width, wheelHeight));
        if (RangeRow.IsVisible)
            RangeRow.Arrange(new Rect(0, y + wheelHeight + RangeGap, finalSize.Width, RangeRow.DesiredSize.Height));
        return finalSize;
    }
}
