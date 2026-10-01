using System.Reactive.Concurrency;
using Avalonia.Threading;
using ReactiveUI.Primitives.Reactive.Concurrency;

namespace Beutl.Helpers;

internal sealed class UiThreadScheduler : LocalScheduler
{
    private const int MaxInlineDepth = 32;
    private int _inlineDepth;

    public static UiThreadScheduler Instance { get; } = new();

    private UiThreadScheduler()
    {
    }

    public override IDisposable Schedule<TState>(TState state, TimeSpan dueTime,
        Func<IScheduler, TState, IDisposable> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        dueTime = Scheduler.Normalize(dueTime);

        // ReactiveProperty bindings rely on synchronous notifications on the UI thread.
        // Bound reentrancy so recursive work yields to the dispatcher instead of overflowing.
        if (dueTime == TimeSpan.Zero && Dispatcher.UIThread.CheckAccess() && _inlineDepth < MaxInlineDepth)
        {
            try
            {
                _inlineDepth++;
                return action(this, state);
            }
            finally
            {
                _inlineDepth--;
            }
        }

        return AvaloniaScheduler.Instance.Schedule((Scheduler: this, State: state, Action: action), dueTime,
            static (_, work) => work.Action(work.Scheduler, work.State));
    }
}
