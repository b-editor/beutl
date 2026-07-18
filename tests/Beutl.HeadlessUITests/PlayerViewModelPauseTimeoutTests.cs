using System.Diagnostics;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PlayerViewModelPauseTimeoutTests
{
    [Test]
    public async Task WaitForPlaybackStopAsync_returns_false_and_logs_when_the_task_never_completes()
    {
        var logger = new CapturingLogger();
        var timeout = TimeSpan.FromMilliseconds(200);
        // A task that never completes stands in for a playback loop stuck in a blocking
        // OS audio/COM call — the exact case the unbounded await would hang on.
        Task neverCompletes = new TaskCompletionSource().Task;

        var sw = Stopwatch.StartNew();
        bool completed = await PlayerViewModel.WaitForPlaybackStopAsync(neverCompletes, timeout, logger, "scene-1");
        sw.Stop();

        Assert.That(completed, Is.False, "the bounded wait must give up instead of hanging");
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)), "the wait must return near the timeout, not hang");
        Assert.That(logger.Errors, Is.Not.Empty, "a timeout must be logged as an error");
    }

    [Test]
    public async Task WaitForPlaybackStopAsync_returns_true_when_the_task_already_completed()
    {
        var logger = new CapturingLogger();

        bool completed = await PlayerViewModel.WaitForPlaybackStopAsync(
            Task.CompletedTask, TimeSpan.FromSeconds(5), logger, "scene-1");

        Assert.That(completed, Is.True);
        Assert.That(logger.Errors, Is.Empty, "no error is logged when the task stops in time");
    }

    [Test]
    public void WaitForPlaybackStopAsync_propagates_the_fault_when_the_task_completes_faulted()
    {
        var logger = new CapturingLogger();
        Task faulted = Task.FromException(new InvalidOperationException("boom"));

        // A task that completed before the timeout still has its fault re-thrown so Pause()'s
        // existing catch can drop and reset it — the pre-existing behaviour must be preserved.
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PlayerViewModel.WaitForPlaybackStopAsync(faulted, TimeSpan.FromSeconds(5), logger, "scene-1"));
    }

    [Test]
    public async Task RunAudioPlaybackWithImmediateStopAsync_stops_audio_while_playback_is_blocked()
    {
        using var playbackCts = new CancellationTokenSource();
        using var playbackEntered = new ManualResetEventSlim();
        using var releasePlayback = new ManualResetEventSlim();
        var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task playback = Task.Run(() => PlayerViewModel.RunAudioPlaybackWithImmediateStopAsync(
            playbackCts.Token,
            () => stopped.TrySetResult(true),
            () =>
            {
                playbackEntered.Set();
                if (!releasePlayback.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("The simulated synchronous buffer composition was not released.");
                }

                return Task.CompletedTask;
            }));

        try
        {
            Assert.That(playbackEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            playbackCts.Cancel();

            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(playback.IsCompleted, Is.False,
                "audio must stop without waiting for synchronous buffer composition to return");
        }
        finally
        {
            releasePlayback.Set();
        }

        await playback.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
            }
        }
    }
}
