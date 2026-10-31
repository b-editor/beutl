using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Editor.Services;

public static class SilenceDetector
{
    // Indices are clip-local and uniform. Missing/non-finite bins are unknown, never silence.
    public static IReadOnlyList<TimeRange> Detect(
        IReadOnlyList<WaveformChunk> chunks, MediaTime duration,
        double thresholdDb, MediaTime minimumDuration, MediaTime padding)
    {
        if (!double.IsFinite(thresholdDb) || thresholdDb > 0)
            throw new ArgumentOutOfRangeException(nameof(thresholdDb));
        if (minimumDuration <= MediaTime.Zero) throw new ArgumentOutOfRangeException(nameof(minimumDuration));
        if (padding < MediaTime.Zero) throw new ArgumentOutOfRangeException(nameof(padding));
        if (chunks.Count == 0 || duration <= MediaTime.Zero) return [];
        double threshold = Math.Pow(10, thresholdDb / 20);
        var ranges = new List<TimeRange>();
        int run = -1;
        for (int i = 0; i <= chunks.Count; i++)
        {
            bool silent = i < chunks.Count && chunks[i].Index == i
                && float.IsFinite(chunks[i].MinValue) && float.IsFinite(chunks[i].MaxValue)
                && Math.Max(Math.Abs(chunks[i].MinValue), Math.Abs(chunks[i].MaxValue)) <= threshold;
            if (silent)
            {
                if (run < 0) run = i;
            }
            else if (run >= 0)
            {
                MediaTime start = BinStart(duration, run, chunks.Count);
                MediaTime end = BinStart(duration, i, chunks.Count);
                if (end - start >= minimumDuration)
                {
                    // Protect speech at either side, but do not retain padding at the clip edges.
                    if (run > 0) start += padding;
                    if (i < chunks.Count) end -= padding;
                    if (end > start) ranges.Add(TimeRange.FromRange(start, end));
                }
                run = -1;
            }
        }
        return ranges;
    }

    // The clip-local start of bin index of count uniform bins over duration; the preview places its window with it too,
    // so the highlighted ranges line up with the bins.
    internal static MediaTime BinStart(MediaTime duration, int index, int count)
        => new((long)((Int128)duration.Flicks * index / count));
}
