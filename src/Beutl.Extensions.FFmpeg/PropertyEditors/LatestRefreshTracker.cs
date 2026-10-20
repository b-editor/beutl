namespace Beutl.Extensions.FFmpeg.PropertyEditors;

/// <summary>
/// Tracks the latest refresh request of a codec property editor so a superseded request cannot
/// apply its now-stale result over the most recent selection. Each request is represented by a
/// <see cref="CancellationToken"/>; <see cref="IsCurrent"/> reports whether a token is still the
/// latest one.
/// </summary>
/// <remarks>
/// Call the mutating members only on the UI thread. Only <see cref="IsCurrent"/> is read from other threads,
/// which works even after disposal because a captured token stays readable once its source is disposed.
/// </remarks>
internal sealed class LatestRefreshTracker : IDisposable
{
    private CancellationTokenSource? _cts;

    /// <summary>Supersedes any in-flight request and begins a new one, returning its token.</summary>
    public CancellationToken StartNew()
    {
        Supersede();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    /// <summary>Supersedes any in-flight request without starting a new one (e.g. a synchronous apply).</summary>
    public void Supersede()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>True while <paramref name="token"/> still represents the latest request.</summary>
    public static bool IsCurrent(CancellationToken token) => !token.IsCancellationRequested;

    public void Dispose() => Supersede();
}
