using Avalonia;
using Avalonia.Controls;

namespace Beutl.Editor.Components.TimelineTab.Views;

// The slots of a filmstrip or a waveform that its scroll viewer shows, with buffer slots on each side.
internal static class TimelineSlotRange
{
    public static (int Start, int End) Compute(
        Visual visual,
        ScrollViewer? scrollViewer,
        int count,
        int buffer,
        double contentWidth = double.NaN,
        double contentOffsetX = 0,
        bool allowOutOfRange = false)
    {
        if (count <= 0)
            return (0, -1);

        double width = visual.Bounds.Width;
        double height = visual.Bounds.Height;
        if (width <= 0 || height <= 0)
            return (0, -1);

        double effectiveWidth = !double.IsNaN(contentWidth) && contentWidth > 0 ? contentWidth : width;
        double slotWidth = effectiveWidth / count;
        if (slotWidth <= 0)
            return (0, -1);

        double minX = 0;
        double maxX = width;

        if (scrollViewer != null)
        {
            var topLeft = visual.TranslatePoint(visual.Bounds.TopLeft, scrollViewer);
            var bottomRight = visual.TranslatePoint(visual.Bounds.BottomRight, scrollViewer);
            if (topLeft.HasValue && bottomRight.HasValue)
            {
                var translatedBounds = new Rect(topLeft.Value, bottomRight.Value);
                minX = Math.Max(0, -translatedBounds.Left);
                maxX = Math.Min(width, scrollViewer.Viewport.Width - translatedBounds.Left);
            }
        }

        if (maxX <= minX)
            return (0, -1);

        int startIndex = (int)Math.Floor((minX - contentOffsetX) / slotWidth) - buffer;
        int endIndex = (int)Math.Ceiling((maxX - contentOffsetX) / slotWidth) + buffer;

        if (!allowOutOfRange)
        {
            startIndex = Math.Max(0, startIndex);
            endIndex = Math.Min(count - 1, endIndex);
        }

        return (startIndex, endIndex);
    }
}
