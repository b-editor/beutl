namespace Beutl.Media.Proxy;

public sealed class ProxyJob
{
    public ProxyJob(
        ProxyFingerprint source,
        ProxyPreset preset,
        IProgress<ProxyJobProgress>? progress = null,
        CancellationToken cancellationToken = default,
        int priority = 0)
    {
        Source = source;
        Preset = preset;
        Progress = progress;
        CancellationToken = cancellationToken;
        Priority = priority;
    }

    public Guid JobId { get; } = Guid.NewGuid();

    public ProxyFingerprint Source { get; }

    public ProxyPreset Preset { get; }

    public IProgress<ProxyJobProgress>? Progress { get; }

    public CancellationToken CancellationToken { get; }

    // Higher values are dispatched first; jobs with equal priority keep arrival (FIFO) order.
    public int Priority { get; }

    public ProxyJobStatus Status { get; internal set; } = ProxyJobStatus.Queued;

    public ProxyJobProgress? LatestProgress { get; internal set; }

    public Exception? Error { get; internal set; }

    public string? StatusMessage { get; internal set; }

    // Non-null when recording the Failed proxy entry itself threw; distinct from the primary Error.
    public Exception? BookkeepingError { get; internal set; }
}

public readonly record struct ProxyJobProgress(double FractionComplete, TimeSpan? Eta);

public enum ProxyJobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Canceled,
    Skipped,
}
