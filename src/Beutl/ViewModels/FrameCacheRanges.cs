using Beutl.Media;

namespace Beutl.ViewModels;

// Converts a time range to the frames the frame cache drops for it: whole frames from the start, rounded
// up at the end.
internal static class FrameCacheRanges
{
    public static (int Start, int End) ToFrameRange(TimeRange range, Rational rate)
    {
        return (Start: (int)range.Start.ToFrame(rate, MidpointRounding.ToNegativeInfinity),
            End: (int)range.End.ToFrame(rate, MidpointRounding.ToPositiveInfinity));
    }
}
