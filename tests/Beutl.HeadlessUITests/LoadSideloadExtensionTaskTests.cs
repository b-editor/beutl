using Beutl.Api.Services;
using Beutl.Services.StartupTasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class LoadSideloadExtensionTaskTests
{
    [Test]
    public async Task Task_CompletesBeforeUserConfirms_ThenLoadsAfterAcceptance()
    {
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var package = new LocalPackage { Name = "Sideload" };
        var loaded = new List<LocalPackage>();
        var task = CreateTask([package], confirmation.Task, loaded.Add);

        await task.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(task.DeferredLoadingTask.IsCompleted, Is.False);
            Assert.That(loaded, Is.Empty);
        });

        confirmation.SetResult(true);
        await task.DeferredLoadingTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(loaded, Is.EqualTo([package]));
    }

    [Test]
    public async Task DeferredLoadingTask_DoesNotLoadAfterDecline()
    {
        var package = new LocalPackage { Name = "Sideload" };
        var loaded = new List<LocalPackage>();
        var task = CreateTask([package], Task.FromResult(false), loaded.Add);

        await task.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await task.DeferredLoadingTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(loaded, Is.Empty);
    }

    [Test]
    public async Task DeferredLoadingTask_ReportsLoadFailures()
    {
        var package = new LocalPackage { Name = "Broken" };
        IReadOnlyList<(LocalPackage, Exception)>? reported = null;
        var task = new LoadSideloadExtensionTask(
            () => [package],
            _ => throw new InvalidOperationException("boom"),
            _ => Task.FromResult(true),
            failures => reported = failures,
            NullLogger.Instance);

        await task.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await task.DeferredLoadingTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(task.Failures, Has.Count.EqualTo(1));
            Assert.That(reported, Has.Count.EqualTo(1));
            Assert.That(reported![0].Item1, Is.SameAs(package));
            Assert.That(reported[0].Item2, Is.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public async Task DeferredLoadingTask_HandlesConfirmationFailure()
    {
        var package = new LocalPackage { Name = "Sideload" };
        var loaded = new List<LocalPackage>();
        var task = CreateTask(
            [package],
            Task.FromException<bool>(new InvalidOperationException("boom")),
            loaded.Add);

        await task.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(
            async () => await task.DeferredLoadingTask.WaitAsync(TimeSpan.FromSeconds(5)),
            Throws.Nothing);
        Assert.That(loaded, Is.Empty);
    }

    private static LoadSideloadExtensionTask CreateTask(
        IReadOnlyList<LocalPackage> packages,
        Task<bool> confirmation,
        Action<LocalPackage> load)
    {
        return new LoadSideloadExtensionTask(
            () => packages,
            load,
            _ => confirmation,
            _ => { },
            NullLogger.Instance);
    }
}
