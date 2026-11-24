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

    internal double ViewportHeight
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            InvalidateMeasure();
        }
    } = double.PositiveInfinity;

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
        double height = Math.Min(availableSize.Height, ViewportHeight);
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
        bool above = placement == AdjustmentsPlacement.Above;
        double scrollerWidth = width;
        if (!above)
        {
            SetCompact(placement == AdjustmentsPlacement.Compact);
            scrollerWidth = placement == AdjustmentsPlacement.Beside ? Math.Min(width, ColumnWidth) : double.PositiveInfinity;
            Scroller.Measure(new Size(scrollerWidth, height));
        }

        double side = above ? 0 : ScrollerExtent(width);
        double top = above ? ScrollerExtent(height) : 0;
        double wheelWidth = Math.Max(0, width - side);
        RangeRow.Measure(new Size(wheelWidth, double.PositiveInfinity));
        if (wheels != null)
        {
            wheels.ViewportHeight = Math.Max(0, height - top - RangeHeight);
            wheels.Measure(new Size(wheelWidth, double.PositiveInfinity));
        }
        Size wheelSize = wheels?.DesiredSize ?? default;
        double contentWidth = Math.Max(wheelSize.Width, RangeRow.DesiredSize.Width);
        double contentHeight = wheelSize.Height + RangeHeight;
        if (Scroller.IsVisible && top + contentHeight > height)
        {
            // Give the adjustments their full height when the outer viewport needs to scroll.
            Scroller.Measure(new Size(scrollerWidth, double.PositiveInfinity));
            top = above ? ScrollerExtent(double.PositiveInfinity) : 0;
        }

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
        if (Scroller.IsVisible)
        {
            Scroller.Arrange(above
                ? new Rect(0, 0, width, Math.Max(0, extent - AboveGap))
                : new Rect(width + BesideGap, 0, Math.Max(0, extent - BesideGap), finalSize.Height));
        }

        ColorWheelsPanel? wheels = Wheels;
        double wheelHeight = wheels?.DesiredSize.Height ?? 0;
        double top = above ? extent : 0;
        double visibleWheelHeight = Math.Min(finalSize.Height, ViewportHeight) - top;
        double y = top + Math.Max(0, (visibleWheelHeight - wheelHeight - RangeHeight) / 2);
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
