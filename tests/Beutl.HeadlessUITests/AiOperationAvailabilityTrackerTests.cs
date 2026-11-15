using System.Collections.Concurrent;
using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiOperationAvailabilityTrackerTests
{
    private static readonly AiOperationAvailabilityRequest s_request =
        new AiOperationAvailabilityRequest.Video(AiOperations.VideoGeneration, 6);

    [AvaloniaTest]
    public void BeforeAnyCheck_TheStateIsUnknownRatherThanRefused()
    {
        using var tracker = new AiOperationAvailabilityTracker(
            new StubService(_ => new TaskCompletionSource<bool>().Task),
            CancellationToken.None);

        Assert.That(tracker.State.Value, Is.EqualTo(AiOperationAvailabilityState.Unknown));
    }

    [AvaloniaTest]
    public async Task AnInFlightCheck_LeavesTheStateUnknownUntilTheServerAnswers()
    {
        var answer = new TaskCompletionSource<bool>();
        using var tracker = new AiOperationAvailabilityTracker(
            new StubService(_ => answer.Task),
            CancellationToken.None,
            TimeSpan.Zero);

        tracker.Check(s_request);
        Assert.That(tracker.State.Value, Is.EqualTo(AiOperationAvailabilityState.Unknown),
            "A pending check must not read as a refusal.");

        answer.SetResult(true);
        await tracker.CurrentCheck!;
        HeadlessTestHelpers.Settle();

        Assert.That(tracker.State.Value, Is.EqualTo(AiOperationAvailabilityState.Available));
    }

    [AvaloniaTest]
    public async Task AFailedCheck_FallsBackToUnknownRatherThanRefusing()
    {
        using var tracker = new AiOperationAvailabilityTracker(
            new StubService(_ => Task.FromException<bool>(new HttpRequestException("offline"))),
            CancellationToken.None,
            TimeSpan.Zero);

        tracker.Check(s_request);
        await tracker.CurrentCheck!;
        HeadlessTestHelpers.Settle();

        Assert.That(tracker.State.Value, Is.EqualTo(AiOperationAvailabilityState.Unknown),
            "A check that could not be made has not refused anything.");
    }

    [AvaloniaTest]
    public async Task ARefusal_IsReportedAsUnavailable()
    {
        using var tracker = new AiOperationAvailabilityTracker(
            new StubService(_ => Task.FromResult(false)),
            CancellationToken.None,
            TimeSpan.Zero);

        tracker.Check(s_request);
        await tracker.CurrentCheck!;
        HeadlessTestHelpers.Settle();

        Assert.That(tracker.State.Value, Is.EqualTo(AiOperationAvailabilityState.Unavailable));
    }

    [AvaloniaTest]
    public async Task AskingAboutNothing_ClearsTheAnswerWithoutRefusing()
    {
        using var tracker = new AiOperationAvailabilityTracker(
            new StubService(_ => Task.FromResult(true)),
            CancellationToken.None,
            TimeSpan.Zero);

        tracker.Check(s_request);
        await tracker.CurrentCheck!;
        HeadlessTestHelpers.Settle();
        Assert.That(tracker.State.Value, Is.EqualTo(AiOperationAvailabilityState.Available));

        // No valid request to ask about — nothing to translate yet, say.
        tracker.Check(null);
        HeadlessTestHelpers.Settle();

        Assert.That(tracker.State.Value, Is.EqualTo(AiOperationAvailabilityState.Unknown));
    }

    // A check that finishes on another thread while a newer check replaces it, or while the tracker is disposed, must
    // not dispose its request under the one cancelling it (#4174). The window is a few instructions wide, so the race
    // runs on more threads than there are cores, where one can be switched out inside it as on a busy CI runner. The
    // call that races the answer starts a little later each round, so the two meet at every point of it. The test awaits
    // the racers, which leaves the UI thread free to apply the states the trackers post to it as they come; waiting on
    // it would keep every one of them, and its tracker, until the test returned.
    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task A_check_that_finishes_as_it_is_cancelled_leaves_its_request_to_the_canceller(bool replace)
    {
        // Before the fix, 2 of 320,000 rounds threw on 8 cores in each case; a round takes a few microseconds.
        const int Rounds = 20_000;
        var failures = new ConcurrentQueue<Exception>();
        Task[] racers = Enumerable.Range(0, Environment.ProcessorCount * 2).Select(_ => Task.Factory.StartNew(() =>
        {
            for (int i = 0; i < Rounds; i++)
            {
                var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var tracker = new AiOperationAvailabilityTracker(
                    new StubService(_ => answer.Task),
                    CancellationToken.None,
                    TimeSpan.Zero);
                tracker.Check(s_request);
                Task first = tracker.CurrentCheck!;
                answer.SetResult(true);
                Thread.SpinWait(i % 512);
                try
                {
                    if (replace)
                        tracker.Check(s_request);

                    tracker.Dispose();
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);
                }

                first.Wait();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        await Task.WhenAll(racers);

        Assert.That(failures.Select(failure => failure.GetType().Name).Distinct(), Is.Empty,
            $"{failures.Count} of {Rounds * racers.Length} rounds threw.");
    }

    private sealed class StubService(
        Func<AiOperationAvailabilityRequest, Task<bool>> answer) : IAiOperationAvailabilityService
    {
        public Task<bool> CheckAsync(
            AiOperationAvailabilityRequest request,
            CancellationToken cancellationToken)
            => answer(request);
    }
}
