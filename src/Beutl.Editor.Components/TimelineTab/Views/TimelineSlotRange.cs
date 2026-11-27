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
        bool allowOutOfRange = false,
        double? layoutWidth = null)
    {
        if (count <= 0)
            return (0, -1);

        double width = layoutWidth ?? visual.Bounds.Width;
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
            var topLeft = visual.TranslatePoint(default, scrollViewer);
            if (topLeft.HasValue)
            {
                minX = Math.Max(0, -topLeft.Value.X);
                maxX = Math.Min(width, scrollViewer.Viewport.Width - topLeft.Value.X);
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
