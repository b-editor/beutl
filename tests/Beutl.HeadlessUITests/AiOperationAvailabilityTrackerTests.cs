using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiOperationAvailabilityTrackerTests
{
    private static readonly AiOperationAvailabilityRequest s_request =
        new AiOperationAvailabilityRequest.Video(6);

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

    private sealed class StubService(
        Func<AiOperationAvailabilityRequest, Task<bool>> answer) : IAiOperationAvailabilityService
    {
        public Task<bool> CheckAsync(
            AiOperationAvailabilityRequest request,
            CancellationToken cancellationToken)
            => answer(request);
    }
}
