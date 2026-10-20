using Avalonia;
using Avalonia.Controls;

namespace Beutl.Editor.Components.TimelineTab.Views;

// The slots of a filmstrip or a waveform that its scroll viewer shows, with buffer slots on each side.
internal static class TimelineSlotRange
{
    public static (int Start, int End) Compute(Visual visual, ScrollViewer? scrollViewer, int count, int buffer)
    {
        if (count <= 0)
            return (0, -1);

        double width = visual.Bounds.Width;
        double height = visual.Bounds.Height;
        if (width <= 0 || height <= 0)
            return (0, -1);

        double slotWidth = width / count;
        int startIndex = 0;
        int endIndex = count - 1;

        if (scrollViewer != null)
        {
            var topLeft = visual.TranslatePoint(visual.Bounds.TopLeft, scrollViewer);
            var bottomRight = visual.TranslatePoint(visual.Bounds.BottomRight, scrollViewer);
            if (topLeft.HasValue && bottomRight.HasValue)
            {
                var translatedBounds = new Rect(topLeft.Value, bottomRight.Value);
                double minX = Math.Max(0, -translatedBounds.Left);
                double maxX = scrollViewer.Viewport.Width - translatedBounds.Left;

                startIndex = Math.Max(0, (int)(minX / slotWidth) - buffer);
                endIndex = Math.Min(count - 1, (int)Math.Ceiling(maxX / slotWidth) + buffer);
            }
        }

        return (startIndex, endIndex);
    }
}
