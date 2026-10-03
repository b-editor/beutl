using System.Threading.Channels;
using Beutl.Threading;
using Microsoft.Extensions.Time.Testing;

namespace Beutl.UnitTests.Threading;

[TestFixture]
public sealed class DispatcherTimeProviderTests
{
    [Test]
    public async Task Schedule_WakesWhenInjectedClockAdvances()
    {
        var time = new TrackingTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var dispatcher = Dispatcher.Spawn(time);
        dispatcher.Thread.IsBackground = true;
        var executed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            dispatcher.Schedule(TimeSpan.FromMinutes(1), () =>
            {
                executed.TrySetResult(dispatcher.CheckAccess());
            });
            // Advance only after the dispatcher has armed its wait, so posting cannot wake it.
            await time.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));

            time.Advance(TimeSpan.FromMinutes(1));

            Assert.That(await executed.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        }
        finally
        {
            dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        }
    }

    [Test]
    public async Task Schedule_WhenClockReachesDeadlineWhileArmingWait_DoesNotRemainBlocked()
    {
        var time = new TrackingTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), advanceBeforeFirstTimer: true);
        var dispatcher = Dispatcher.Spawn(time);
        dispatcher.Thread.IsBackground = true;
        var executed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            dispatcher.Schedule(TimeSpan.FromMinutes(1), () =>
            {
                executed.TrySetResult(dispatcher.CheckAccess());
            });
            await time.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(await executed.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        }
        finally
        {
            dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Schedule_BeyondMaximumWaitDelay_RearmsUntilDeadline(bool advanceBeforeFirstTimer)
    {
        var time = new TrackingTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), advanceBeforeFirstTimer);
        var dispatcher = Dispatcher.Spawn(time);
        dispatcher.Thread.IsBackground = true;
        var executed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        TimeSpan delay = TimeSpan.FromDays(30);
        TimeSpan maximumWait = TimeSpan.FromMilliseconds(int.MaxValue);

        try
        {
            dispatcher.Schedule(delay, () => executed.TrySetResult(dispatcher.CheckAccess()));
            Assert.That(await time.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(maximumWait));

            if (!advanceBeforeFirstTimer)
                time.Advance(maximumWait);
            // Posting can cancel a wait before Schedule returns; skip those earlier notifications.
            TimeSpan nextWait;
            do
            {
                nextWait = await time.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));
            } while (nextWait == maximumWait);
            Assert.That(nextWait, Is.EqualTo(delay - maximumWait));
            Assert.That(executed.Task.IsCompleted, Is.False);

            time.Advance(delay - maximumWait);
            Assert.That(await executed.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        }
        finally
        {
            dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Schedule_WhenClockAdvancesPartwayWhileArmingWait_RearmsRemainingDelay(bool advanceWhileRearming)
    {
        TimeSpan advance = TimeSpan.FromSeconds(30);
        TimeSpan rearmAdvance = advanceWhileRearming ? TimeSpan.FromSeconds(10) : TimeSpan.Zero;
        var time = new TrackingTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            partialAdvanceBeforeFirstTimer: advance, advanceBeforeFirstChange: rearmAdvance);
        var dispatcher = Dispatcher.Spawn(time);
        dispatcher.Thread.IsBackground = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task initial = dispatcher.InvokeAsync(() =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        });

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Finish posting before the dispatcher arms its first timed wait.
            dispatcher.Schedule(TimeSpan.FromMinutes(1), () => executed.TrySetResult(dispatcher.CheckAccess()));
            release.TrySetResult();
            await initial.WaitAsync(TimeSpan.FromSeconds(5));
            await time.ClockCheckedAfterTimer.WaitAsync(TimeSpan.FromSeconds(5));

            TimeSpan remaining;
            do
            {
                remaining = await time.TimerCreated.WaitAsync(TimeSpan.FromSeconds(5));
            } while (remaining != advance - rearmAdvance);

            time.Advance(advance - rearmAdvance);

            Assert.That(await executed.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        }
        finally
        {
            release.TrySetResult();
            dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        }
    }

    private sealed class TrackingTimeProvider(
        DateTimeOffset start,
        bool advanceBeforeFirstTimer = false,
        TimeSpan? partialAdvanceBeforeFirstTimer = null,
        TimeSpan? advanceBeforeFirstChange = null) : TimeProvider
    {
        private readonly FakeTimeProvider _time = new(start);
        private readonly Channel<TimeSpan> _timersCreated = Channel.CreateUnbounded<TimeSpan>();
        private readonly TaskCompletionSource _clockCheckedAfterTimer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _timerCount;

        public Task<TimeSpan> TimerCreated => _timersCreated.Reader.ReadAsync().AsTask();

        public Task ClockCheckedAfterTimer => _clockCheckedAfterTimer.Task;

        public override DateTimeOffset GetUtcNow()
        {
            DateTimeOffset now = _time.GetUtcNow();
            if (Volatile.Read(ref _timerCount) > 0)
                _clockCheckedAfterTimer.TrySetResult();
            return now;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (Interlocked.Increment(ref _timerCount) == 1)
            {
                if (advanceBeforeFirstTimer)
                    _time.Advance(dueTime);
                else if (partialAdvanceBeforeFirstTimer is { } advance)
                    _time.Advance(advance);
            }
            ITimer timer = _time.CreateTimer(callback, state, dueTime, period);
            _timersCreated.Writer.TryWrite(dueTime);
            return new TrackingTimer(timer, _timersCreated.Writer, _time, advanceBeforeFirstChange);
        }

        public void Advance(TimeSpan amount) => _time.Advance(amount);

        private sealed class TrackingTimer(
            ITimer timer, ChannelWriter<TimeSpan> notifications,
            FakeTimeProvider time, TimeSpan? advanceBeforeFirstChange) : ITimer
        {
            private int _changeCount;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (Interlocked.Increment(ref _changeCount) == 1 && advanceBeforeFirstChange is { } advance)
                    time.Advance(advance);
                bool changed = timer.Change(dueTime, period);
                if (changed)
                    notifications.TryWrite(dueTime);
                return changed;
            }

            public void Dispose() => timer.Dispose();

            public ValueTask DisposeAsync() => timer.DisposeAsync();
        }
    }
}
