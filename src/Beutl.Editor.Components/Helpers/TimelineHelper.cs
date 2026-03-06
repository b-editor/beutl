using Avalonia;

namespace Beutl.Editor.Components.Helpers;

public static class TimelineHelper
{
    public enum MouseFlags
    {
        Free,
        SeekBarPressed,
        RangeSelectionPressed,
        EndingBarMarkerPressed,
        StartingBarMarkerPressed
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
