using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg.Decoding;

internal interface IIdleSuspendableReader
{
    long LastAccessTicks { get; }

    long PixelCount { get; }

    bool IsSuspended { get; }

    // Must not block, and must decline when the reader was used after expectedLastAccessTicks was read.
    bool TrySuspend(long expectedLastAccessTicks);
}

// Every decoding reader lives in the single FFmpeg worker process, and the scene compositor keeps the
// readers of all elements it has rendered, so readers the timeline has moved past are suspended here
// (worker decoder and shared memory released) and reopen on their next read.
internal sealed class FFmpegReaderIdleTracker
{
    private static readonly ILogger s_logger = Log.CreateLogger<FFmpegReaderIdleTracker>();
    private static readonly TimeSpan s_sweepInterval = TimeSpan.FromSeconds(1);

    private readonly Lock _lock = new();
    // Held strongly so that a reader which is never disposed is still suspended: its finalizer cannot
    // reach the worker, so dropping it here would leak the worker-side decoder for the process lifetime.
    private readonly HashSet<IIdleSuspendableReader> _readers = new(ReferenceEqualityComparer.Instance);
    private readonly FFmpegReaderIdlePolicy.Limits _limits;
    private readonly bool _sweepPeriodically;
    private Timer? _timer;
    private int _sweeping;

    internal FFmpegReaderIdleTracker(FFmpegReaderIdlePolicy.Limits limits, bool sweepPeriodically)
    {
        _limits = limits;
        _sweepPeriodically = sweepPeriodically;
    }

    public static FFmpegReaderIdleTracker Shared { get; } = new(FFmpegReaderIdlePolicy.DefaultLimits, sweepPeriodically: true);

    internal int TrackedCount
    {
        get
        {
            lock (_lock)
                return _readers.Count;
        }
    }

    public void Track(IIdleSuspendableReader reader)
    {
        lock (_lock)
        {
            _readers.Add(reader);
            if (_sweepPeriodically)
            {
                _timer ??= new Timer(
                    static state => ((FFmpegReaderIdleTracker)state!).OnTimer(),
                    this, s_sweepInterval, s_sweepInterval);
            }
        }
    }

    public void Untrack(IIdleSuspendableReader reader)
    {
        lock (_lock)
            _readers.Remove(reader);
    }

    internal void Sweep(long nowTicks)
    {
        IIdleSuspendableReader[] readers;
        lock (_lock)
            readers = [.. _readers];

        if (readers.Length == 0)
            return;

        var candidates = new FFmpegReaderIdlePolicy.Candidate[readers.Length];
        for (int i = 0; i < readers.Length; i++)
            candidates[i] = new(readers[i].LastAccessTicks, readers[i].PixelCount);

        int suspended = 0;
        foreach (int index in FFmpegReaderIdlePolicy.SelectForSuspension(candidates, nowTicks, _limits))
        {
            if (readers[index].TrySuspend(candidates[index].LastAccessTicks))
                suspended++;
        }

        if (suspended == 0)
            return;

        // A reader registers again when it resumes, which sets IsSuspended back before calling Track.
        lock (_lock)
            _readers.RemoveWhere(reader => reader.IsSuspended);

        s_logger.LogDebug("Suspended {Count} idle FFmpeg readers", suspended);
    }

    private void OnTimer()
    {
        if (Interlocked.Exchange(ref _sweeping, 1) != 0)
            return;

        try
        {
            Sweep(Environment.TickCount64);
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to suspend idle FFmpeg readers");
        }
        finally
        {
            Volatile.Write(ref _sweeping, 0);
        }
    }
}
