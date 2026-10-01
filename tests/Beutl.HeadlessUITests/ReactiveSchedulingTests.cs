using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Helpers;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ReactiveSchedulingTests
{
    [AvaloniaTest]
    public void ReactiveUI_UsesApplicationUIScheduler()
    {
        Assert.That(ReactiveUI.Reactive.RxSchedulers.MainThreadScheduler, Is.SameAs(UiThreadScheduler.Instance));
    }

    [AvaloniaTest]
    public void ReactiveProperty_DefaultScheduler_DeliversUIThreadChangesSynchronously()
    {
        using var property = new ReactiveProperty<int>();
        int observed = 0;
        property.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(property.Value)) observed = property.Value;
        };

        property.Value = 42;

        Assert.That(observed, Is.EqualTo(42));
    }

    [AvaloniaTest]
    public async Task ReactiveProperty_DefaultScheduler_DeliversBackgroundChangesOnUIThread()
    {
        using var property = new ReactiveProperty<int>();
        var notification = new TaskCompletionSource<(int Value, bool OnUIThread)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        property.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(property.Value))
                notification.TrySetResult((property.Value, Dispatcher.UIThread.CheckAccess()));
        };

        await Task.Run(() => property.Value = 42);
        var result = await notification.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value, Is.EqualTo(42));
            Assert.That(result.OnUIThread, Is.True);
        }
    }

    [AvaloniaTest]
    public async Task ObservableInterval_DeliversTicksOnUIThread()
    {
        var ticks = new List<long>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool allTicksOnUIThread = true;
        using IDisposable subscription = Observable
            .Interval(TimeSpan.FromMilliseconds(10), UiThreadScheduler.Instance)
            .Take(2)
            .Subscribe(value =>
            {
                ticks.Add(value);
                allTicksOnUIThread &= Dispatcher.UIThread.CheckAccess();
            }, error => completed.TrySetException(error), () => completed.TrySetResult());

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ticks, Is.EqualTo(new long[] { 0, 1 }));
            Assert.That(allTicksOnUIThread, Is.True);
        }
    }

    [AvaloniaTest]
    public async Task RecursiveScheduling_YieldsWithoutLosingUIThreadOrScheduler()
    {
        const int Iterations = 256;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int depth = 0;
        int maximumDepth = 0;
        bool allCallsOnUIThread = true;
        bool sameScheduler = true;

        IDisposable Run(IScheduler scheduler, int iteration)
        {
            depth++;
            maximumDepth = Math.Max(maximumDepth, depth);
            allCallsOnUIThread &= Dispatcher.UIThread.CheckAccess();
            sameScheduler &= ReferenceEquals(scheduler, UiThreadScheduler.Instance);
            try
            {
                if (iteration == Iterations)
                {
                    completed.TrySetResult();
                    return Disposable.Empty;
                }

                return scheduler.Schedule(iteration + 1, Run);
            }
            finally
            {
                depth--;
            }
        }

        using IDisposable subscription = UiThreadScheduler.Instance.Schedule(0, Run);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(maximumDepth, Is.LessThan(Iterations));
            Assert.That(allCallsOnUIThread, Is.True);
            Assert.That(sameScheduler, Is.True);
        }
    }

    [AvaloniaTest]
    public void DisposingPendingBackgroundWork_PreventsItsCallback()
    {
        bool invoked = false;
        // Keep the UI thread occupied until the queued work has been cancelled.
        Task.Run(() =>
        {
            using IDisposable work = UiThreadScheduler.Instance.Schedule(() => invoked = true);
        }).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

        Dispatcher.UIThread.RunJobs();

        Assert.That(invoked, Is.False);
    }
}
