using Avalonia.Headless.NUnit;
using Beutl.Services.Tutorials;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class TutorialServiceAsyncPrerequisiteTests
{
    [AvaloniaTest]
    public async Task StartTutorialWaitsForAsyncCanStartAndStopsWhenFalse()
    {
        await TestReset.ResetShellAsync();
        var gate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new TutorialServiceHandler();
        service.Register(Tutorial("async-false", () => gate.Task));

        Task start = service.StartTutorial("async-false");
        Assert.That(start.IsCompleted, Is.False);
        Assert.That(service.GetCurrentState(), Is.Null);

        gate.SetResult(false);
        await start.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(service.GetCurrentState(), Is.Null);
    }

    [AvaloniaTest]
    public async Task StartTutorialRechecksCanStartAfterFulfillingPrerequisites()
    {
        await TestReset.ResetShellAsync();
        bool ready = false;
        int checks = 0;
        int fulfills = 0;
        var service = new TutorialServiceHandler();
        service.Register(Tutorial(
            "async-recheck",
            () =>
            {
                checks++;
                return Task.FromResult(ready);
            },
            () =>
            {
                fulfills++;
                ready = true;
                return Task.FromResult(true);
            }));

        await service.StartTutorial("async-recheck", autoFulfillPrerequisites: true)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(checks, Is.EqualTo(2));
            Assert.That(fulfills, Is.EqualTo(1));
            Assert.That(service.GetCurrentState()?.Definition.Id, Is.EqualTo("async-recheck"));
        });
        service.CancelTutorial();
    }

    [AvaloniaTest]
    public async Task StartTutorialDoesNotStartWhenAsyncCanStartThrows()
    {
        await TestReset.ResetShellAsync();
        var service = new TutorialServiceHandler();
        service.Register(Tutorial(
            "async-error",
            static () => Task.FromException<bool>(new InvalidOperationException("failed"))));

        Assert.DoesNotThrowAsync(async () =>
            await service.StartTutorial("async-error").WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(service.GetCurrentState(), Is.Null);
    }

    private static TutorialDefinition Tutorial(
        string id,
        Func<Task<bool>> canStart,
        Func<Task<bool>>? fulfill = null)
        => new()
        {
            Id = id,
            Title = id,
            Description = id,
            CanStart = canStart,
            FulfillPrerequisites = fulfill,
            Steps =
            [
                new TutorialStep
                {
                    Id = "step",
                    Title = "Step",
                    Content = "Content",
                },
            ],
        };
}
