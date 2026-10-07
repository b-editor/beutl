namespace Beutl.Extensions.FFmpeg.Decoding;

// Chooses which open readers to suspend. Readers used within the grace period are left alone so the
// sources of the frame being rendered never reopen; among the rest, the most recently used are kept
// warm within a reader and pixel budget (decoder and ring buffer memory scale with the frame size).
internal static class FFmpegReaderIdlePolicy
{
    public static Limits DefaultLimits { get; } = new(
        IdleGraceMilliseconds: 5000,
        MaxIdleReaders: 8,
        MaxIdlePixels: 4L * 3840 * 2160);

    public static List<int> SelectForSuspension(ReadOnlySpan<Candidate> readers, long nowTicks, Limits limits)
    {
        var idle = new List<int>();
        for (int i = 0; i < readers.Length; i++)
        {
            if (nowTicks - readers[i].LastAccessTicks >= limits.IdleGraceMilliseconds)
                idle.Add(i);
        }

        Candidate[] snapshot = readers.ToArray();
        idle.Sort((a, b) => snapshot[b].LastAccessTicks.CompareTo(snapshot[a].LastAccessTicks));

        var suspend = new List<int>();
        int keptReaders = 0;
        long keptPixels = 0;
        foreach (int index in idle)
        {
            long pixels = snapshot[index].PixelCount;
            if (keptReaders < limits.MaxIdleReaders && keptPixels + pixels <= limits.MaxIdlePixels)
            {
                keptReaders++;
                keptPixels += pixels;
            }
            else
            {
                suspend.Add(index);
            }
        }

        return suspend;
    }

    public readonly record struct Candidate(long LastAccessTicks, long PixelCount);

    public sealed record Limits(long IdleGraceMilliseconds, int MaxIdleReaders, long MaxIdlePixels);
}
