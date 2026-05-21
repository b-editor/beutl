using Beutl.Language;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services;

public sealed class ElementNudgeService : IElementNudgeService
{
    // Empirical: long enough to bridge typical key-repeat intervals (~30-50ms)
    // but short enough to keep two intentional taps from collapsing into one
    // history entry.
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(300);

    private static readonly ILogger s_logger = Log.CreateLogger<ElementNudgeService>();

    private readonly HistoryManager _historyManager;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private bool _pending;
    // volatile so a Nudge() racing with Dispose() on another thread sees the
    // flag immediately. The lock-protected re-check inside Schedule() is the
    // primary guard against ObjectDisposedException on _timer.Change.
    private volatile bool _disposed;

    public ElementNudgeService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
        _timer = new Timer(OnTimerTick, state: null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Nudge(Scene scene, IReadOnlyList<Element> targets, int frames)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0 || frames == 0 || _disposed) return;

        int rate = SceneTimeRangeService.GetFrameRate(scene);

        // Anchor on the leftmost element so the resulting delta lands on the
        // frame grid even when callers have off-grid starts.
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
        // _disposed is volatile, but the lock pairs Flush()/Schedule() against
        // Dispose() so a concurrent Schedule cannot reach _timer.Change after
        // _timer.Dispose has run.
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
            // Recheck under the lock: Dispose may have flipped _disposed between
            // the unlocked guard in Nudge() and reaching this line. Without this
            // recheck _timer.Change throws ObjectDisposedException.
            if (_disposed) return;

            _pending = true;
            _timer.Change(DebounceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTimerTick(object? state)
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
