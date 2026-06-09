using Beutl.Language;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services;

public sealed class ElementNudgeService : IElementNudgeService
{
    // Empirical: bridges key-repeat (~30-50ms) without collapsing two
    // intentional taps into one history entry.
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(300);

    private static readonly ILogger s_logger = Log.CreateLogger<ElementNudgeService>();

    private readonly HistoryManager _historyManager;
    private readonly Action<Action>? _postToUi;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private bool _pending;
    // volatile for a quick cross-thread read; the lock-protected re-check in
    // Schedule() is the real guard against ObjectDisposedException on _timer.
    private volatile bool _disposed;

    /// <param name="postToUi">
    /// Posts the deferred commit onto the UI thread, since the debounce timer
    /// fires on a thread-pool thread but <see cref="HistoryManager.Commit"/>
    /// must run on the UI thread. Pass <see langword="null"/> (default) to
    /// commit inline on the timer thread — tests drive <see cref="Flush"/>
    /// synchronously this way.
    /// </param>
    public ElementNudgeService(HistoryManager historyManager, Action<Action>? postToUi = null)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
        _postToUi = postToUi;
        _timer = new Timer(OnTimerTick, state: null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Nudge(Scene scene, IReadOnlyList<Element> targets, int frames)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0 || frames == 0 || _disposed) return;

        int rate = SceneTimeRangeService.GetFrameRate(scene);

        // Anchor on the leftmost element so the delta lands on the frame grid
        // even when callers have off-grid starts.
        Element anchor = targets
            .OrderBy(e => e.Start)
            .ThenBy(e => e.ZIndex)
            .First();
        TimeSpan anchoredStart = anchor.Start.RoundToRate(rate) + frames.ToTimeSpan(rate);
        if (anchoredStart < TimeSpan.Zero) return;

        TimeSpan delta = anchoredStart - anchor.Start;
        if (delta == TimeSpan.Zero) return;

        scene.MoveChildren(0, delta, targets.ToArray());
        Schedule();
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (!_pending) return;
            _pending = false;
            if (!_disposed)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        CommitPending();
    }

    public void Dispose()
    {
        // Lock pairs Flush()/Schedule() against Dispose() so a concurrent
        // Schedule cannot reach _timer.Change after _timer.Dispose.
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        Flush();
        _timer.Dispose();
    }

    private void Schedule()
    {
        lock (_gate)
        {
            // Recheck under the lock: Dispose may have flipped _disposed since
            // the unlocked guard in Nudge(), else _timer.Change would throw.
            if (_disposed) return;

            _pending = true;
            _timer.Change(DebounceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTimerTick(object? state)
    {
        // Runs on a thread-pool thread; marshal the drain to the UI thread when
        // a post is available so the commit serializes with other UI edits. The
        // _gate re-check in DrainPending guards against a concurrent Flush/Dispose.
        if (_postToUi is { } post)
        {
            post(DrainPending);
        }
        else
        {
            DrainPending();
        }
    }

    private void DrainPending()
    {
        lock (_gate)
        {
            if (!_pending) return;
            _pending = false;
        }

        CommitPending();
    }

    private void CommitPending()
    {
        try
        {
            _historyManager.Commit(CommandNames.MoveElement);
        }
        catch (ObjectDisposedException ex)
        {
            s_logger.LogWarning(ex, "Pending nudge commit dropped: HistoryManager already disposed.");
        }
    }
}
