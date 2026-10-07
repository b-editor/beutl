using Beutl.Media;

namespace Beutl.ViewModels;

// Converts a time range to the frames the frame cache drops for it: whole frames from the start, rounded
// up at the end.
internal static class FrameCacheRanges
{
    public static (int Start, int End) ToFrameRange(TimeRange range, int rate)
    {
        return (Start: (int)range.Start.ToFrameNumber(rate), End: (int)Math.Ceiling(range.End.ToFrameNumber(rate)));
    }
}
