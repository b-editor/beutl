using System.Reactive.Concurrency;
using System.Reactive.Disposables;

using Beutl.Threading;

namespace Beutl.Rendering;

public static class RenderThread
{
    public static Dispatcher Dispatcher { get; } = Dispatcher.Spawn(() =>
    {
        Thread.CurrentThread.Name = "Beutl.RenderThread";
        Thread.CurrentThread.IsBackground = true;
    });

    public static IScheduler Scheduler { get; } = new RenderThreadScheduler();

    private class RenderThreadScheduler : LocalScheduler
    {
        private const int MaxReentrantSchedules = 32;

        private int _reentrancyGuard;

        public override IDisposable Schedule<TState>(TState state, TimeSpan dueTime, Func<IScheduler, TState, IDisposable> action)
        {
            Func<IScheduler, TState, IDisposable> action2 = action;
            TState state2 = state;

            IDisposable PostOnDispatcher()
            {
                var composite2 = new CompositeDisposable(2);
                var cancellation = new CancellationDisposable();
                Dispatcher.Dispatch(() =>
                {
                    if (!cancellation.Token.IsCancellationRequested)
                    {
                        composite2.Add(action2(this, state2));
                    }
                }, DispatchPriority.Low);
                composite2.Add(cancellation);
                return composite2;
            }

            if (dueTime == TimeSpan.Zero)
            {
                if (!Dispatcher.CheckAccess())
                {
                    return PostOnDispatcher();
                }

                if (_reentrancyGuard >= MaxReentrantSchedules)
                {
                    return PostOnDispatcher();
                }

                try
                {
                    _reentrancyGuard++;
                    return action2(this, state2);
                }
                finally
                {
                    _reentrancyGuard--;
                }
            }

            var composite = new CompositeDisposable(2);
            var cts = new CancellationTokenSource();
            Dispatcher.Schedule(dueTime, () =>
            {
                composite.Add(action2(this, state2));
            }, ct: cts.Token);
            composite.Add(Disposable.Create(cts, cts => cts.Cancel()));

            return composite;
        }
    }
}
