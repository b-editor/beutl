namespace Beutl.Extensions.FFmpeg.Decoding;

// Chooses which open readers to suspend. Readers used within the grace period are left alone so the
// sources of the frame being rendered never reopen; among the rest, the most recently used are kept
// warm within a reader and memory budget (decoder memory grows with the ring buffer's frame size).
internal static class FFmpegReaderIdlePolicy
{
    // Four 4K SDR readers, each holding four BGRA ring buffer slots with 64 bytes of padding. Spelled out
    // instead of using FFmpegVideoSlotSizing, which the benchmarks compile into another namespace.
    public static Limits DefaultLimits { get; } = new(
        IdleGraceMilliseconds: 5000,
        MaxIdleReaders: 8,
        MaxIdleBytes: 4L * 4 * (3840L * 2160 * 4 + 64));

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
        long keptBytes = 0;
        foreach (int index in idle)
        {
            long bytes = snapshot[index].MemoryBytes;
            if (keptReaders < limits.MaxIdleReaders && keptBytes + bytes <= limits.MaxIdleBytes)
            {
                keptReaders++;
                keptBytes += bytes;
            }
            else
            {
                suspend.Add(index);
            }
        }

        return suspend;
    }

    public readonly record struct Candidate(long LastAccessTicks, long MemoryBytes);

    public sealed record Limits(long IdleGraceMilliseconds, int MaxIdleReaders, long MaxIdleBytes);
}
