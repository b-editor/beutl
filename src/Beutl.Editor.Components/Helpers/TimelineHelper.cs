using Avalonia;

using Beutl.Configuration;

namespace Beutl.Editor.Components.Helpers;

public static class TimelineHelper
{
    public static double? CalculateAutoScrollOffset(
        double seekBarPixel,
        double viewportWidth,
        double currentOffsetX,
        TimelineAutoScrollMode mode)
    {
        double newOffsetX;

        if (mode == TimelineAutoScrollMode.AlwaysFollow)
        {
            newOffsetX = seekBarPixel - (viewportWidth / 2);
        }
        else // PageScroll
        {
            if (seekBarPixel >= currentOffsetX && seekBarPixel <= currentOffsetX + viewportWidth)
                return null;

            if (seekBarPixel > currentOffsetX + viewportWidth)
            {
                newOffsetX = seekBarPixel - (viewportWidth * 0.1);
            }
            else
            {
                newOffsetX = seekBarPixel - (viewportWidth * 0.9);
            }
        }

        return Math.Max(0, newOffsetX);
    }

    public enum MouseFlags
    {
        Free,
        SeekBarPressed,
        RangeSelectionPressed,
        EndingBarMarkerPressed,
        StartingBarMarkerPressed,
        MarkerPressed
    }

    private const int MarkerHeight = 18;
    private const int MarkerWidth = 4;

    public static bool IsPointInTimelineScaleMarker(double x, double y, double startingBarX, double endingBarX)
    {
        return IsPointInTimelineScaleStartingMarker(x, y, startingBarX) ||
               IsPointInTimelineScaleEndingMarker(x, y, endingBarX);
    }

    public static bool IsPointInTimelineScaleStartingMarker(double x, double y, double startingBarX)
    {
        var startRect = new Rect(startingBarX, 0, MarkerWidth, MarkerHeight);
        return startRect.Contains(new Point(x, y));
    }

    public static bool IsPointInTimelineScaleEndingMarker(double x, double y, double endingBarX)
    {
        var endRect = new Rect(endingBarX - MarkerWidth, 0, MarkerWidth, MarkerHeight);
        return endRect.Contains(new Point(x, y));
    }
}
