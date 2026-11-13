using System.Diagnostics;

using Beutl.Api;
using Beutl.Api.Services;
using Beutl.PackageTools.UI.Models;

using PackageToolsMainViewModel = Beutl.PackageTools.UI.ViewModels.MainViewModel;

namespace Beutl.UnitTests.PackageTools;

[TestFixture]
public sealed class MainViewModelLifetimeTests
{
    [Test]
    public async Task DisposeAsync_CancelsAndDrainsBlockedInitializationBeforeDisposal()
    {
        var initializationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int cancelRequestsCount = 0;
        int disposeResourcesCount = 0;
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var viewModel = CreateViewModel(
            httpClient,
            app,
            async cancellationToken =>
            {
                initializationStarted.TrySetResult();
                await ObserveCancellationAndWaitToFinish(
                    cancellationToken,
                    cancellationObserved,
                    releaseInitialization);
            },
            () => Interlocked.Increment(ref cancelRequestsCount),
            () =>
            {
                Interlocked.Increment(ref disposeResourcesCount);
                return ValueTask.CompletedTask;
            });

        try
        {
            await initializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task firstDispose = viewModel.DisposeAsync().AsTask();
            Task secondDispose = viewModel.DisposeAsync().AsTask();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cancelRequestsCount, Is.EqualTo(1));
                Assert.That(disposeResourcesCount, Is.Zero);
                Assert.That(firstDispose.IsCompleted, Is.False);
                Assert.That(secondDispose.IsCompleted, Is.False);
            }

            releaseInitialization.TrySetResult();
            await Task.WhenAll(firstDispose, secondDispose).WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(viewModel.InitializationTask.IsCompletedSuccessfully, Is.True);
                Assert.That(disposeResourcesCount, Is.EqualTo(1));
            }
        }
        finally
        {
            releaseInitialization.TrySetResult();
            await viewModel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task DisposeAsync_DrainsActiveOperationsOnceAndSuppressesNavigationCompletions()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int cancelRequestsCount = 0;
        int disposeResourcesCount = 0;
        int navigationCount = 0;
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var viewModel = CreateViewModel(
            httpClient,
            app,
            static _ => Task.CompletedTask,
            () => Interlocked.Increment(ref cancelRequestsCount),
            () =>
            {
                Interlocked.Increment(ref disposeResourcesCount);
                return ValueTask.CompletedTask;
            });

        try
        {
            await viewModel.InitializationTask.WaitAsync(TimeSpan.FromSeconds(5));
            Task firstOperation = viewModel.RunOperationAsync(
                cancellationToken => BlockUntilCanceledThenReleased(
                    cancellationToken,
                    firstStarted,
                    firstCancellation,
                    releaseFirst),
                () => Interlocked.Increment(ref navigationCount),
                CancellationToken.None);
            Task secondOperation = viewModel.RunOperationAsync(
                cancellationToken => BlockUntilCanceledThenReleased(
                    cancellationToken,
                    secondStarted,
                    secondCancellation,
                    releaseSecond),
                () => Interlocked.Increment(ref navigationCount),
                CancellationToken.None);
            await Task.WhenAll(firstStarted.Task, secondStarted.Task).WaitAsync(TimeSpan.FromSeconds(5));

            Task firstDispose = viewModel.DisposeAsync().AsTask();
            Task secondDispose = viewModel.DisposeAsync().AsTask();
            await Task.WhenAll(firstCancellation.Task, secondCancellation.Task)
                .WaitAsync(TimeSpan.FromSeconds(5));

            releaseFirst.TrySetResult();
            await firstOperation.WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cancelRequestsCount, Is.EqualTo(1));
                Assert.That(disposeResourcesCount, Is.Zero);
                Assert.That(navigationCount, Is.Zero);
                Assert.That(firstDispose.IsCompleted, Is.False);
                Assert.That(secondDispose.IsCompleted, Is.False);
            }

            releaseSecond.TrySetResult();
            await Task.WhenAll(secondOperation, firstDispose, secondDispose)
                .WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(disposeResourcesCount, Is.EqualTo(1));
                Assert.That(navigationCount, Is.Zero);
                Assert.That(
                    () => viewModel.RunOperationAsync(
                        static _ => Task.CompletedTask,
                        static () => { },
                        CancellationToken.None),
                    Throws.TypeOf<ObjectDisposedException>());
            }
        }
        finally
        {
            releaseFirst.TrySetResult();
            releaseSecond.TrySetResult();
            await viewModel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public async Task DisposeAsync_StartsBeutlOnceAfterTheResourcesOnlyWhenTheUserAsked(bool asked)
    {
        var events = new List<string>();
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var viewModel = new PackageToolsMainViewModel(
            httpClient,
            app,
            new ChangesModel(),
            [],
            [],
            initialize: static _ => Task.CompletedTask,
            cancelPendingRequests: static () => { },
            disposeResources: () =>
            {
                lock (events) events.Add("dispose");
                return ValueTask.CompletedTask;
            },
            startBeutl: () =>
            {
                lock (events) events.Add("start");
            });

        await viewModel.InitializationTask.WaitAsync(TimeSpan.FromSeconds(5));
        if (asked)
        {
            viewModel.StartBeutlWhenClosed();
        }

        await Task.WhenAll(viewModel.DisposeAsync().AsTask(), viewModel.DisposeAsync().AsTask())
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(events, Is.EqualTo(asked ? new[] { "dispose", "start" } : new[] { "dispose" }));
    }

    [Test]
    public async Task ShouldStartAutomaticallyAsync_IsTrueOnceInitializationFinishesWhenBeutlRestarts()
    {
        var releaseInitialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var viewModel = new PackageToolsMainViewModel(
            httpClient,
            app,
            new ChangesModel(),
            [],
            [],
            initialize: _ => releaseInitialization.Task,
            cancelPendingRequests: static () => { },
            disposeResources: static () => ValueTask.CompletedTask,
            startsAutomatically: true);

        try
        {
            Task<bool> decision = viewModel.ShouldStartAutomaticallyAsync();
            Assert.That(decision.IsCompleted, Is.False, "The changes are not known before initialization finishes.");

            releaseInitialization.TrySetResult();
            Assert.That(await decision.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
        }
        finally
        {
            releaseInitialization.TrySetResult();
            await viewModel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task ShouldStartAutomaticallyAsync_IsFalseWithoutARestart()
    {
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var viewModel = CreateViewModel(
            httpClient,
            app,
            static _ => Task.CompletedTask,
            static () => { },
            static () => ValueTask.CompletedTask);

        try
        {
            Assert.That(await viewModel.ShouldStartAutomaticallyAsync().WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
        }
        finally
        {
            await viewModel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task ShouldStartAutomaticallyAsync_IsFalseWhenClosedDuringInitialization()
    {
        var releaseInitialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var viewModel = new PackageToolsMainViewModel(
            httpClient,
            app,
            new ChangesModel(),
            [],
            [],
            initialize: _ => releaseInitialization.Task,
            cancelPendingRequests: static () => { },
            disposeResources: static () => ValueTask.CompletedTask,
            startsAutomatically: true);

        Task<bool> decision = viewModel.ShouldStartAutomaticallyAsync();
        Task dispose = viewModel.DisposeAsync().AsTask();
        releaseInitialization.TrySetResult();

        Assert.That(await decision.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task AnotherPackageToolsProcess_KeepsTheChangesFromStartingAutomatically()
    {
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using Process self = Process.GetCurrentProcess();
        using Process other = Process.GetCurrentProcess();
        var viewModel = new PackageToolsMainViewModel(
            httpClient,
            app,
            new ChangesModel(),
            [],
            [self, other],
            cancelPendingRequests: static () => { },
            disposeResources: static () => ValueTask.CompletedTask,
            startsAutomatically: true);

        try
        {
            await viewModel.InitializationTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(viewModel.AreOthersRunning.Value, Is.True);
            Assert.That(await viewModel.ShouldStartAutomaticallyAsync().WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
        }
        finally
        {
            await viewModel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static PackageToolsMainViewModel CreateViewModel(
        HttpClient httpClient,
        BeutlApiApplication app,
        Func<CancellationToken, Task> initialize,
        Action cancelPendingRequests,
        Func<ValueTask> disposeResources)
    {
        return new PackageToolsMainViewModel(
            httpClient,
            app,
            new ChangesModel(),
            [],
            [],
            initialize,
            cancelPendingRequests,
            disposeResources);
    }

    private static async Task BlockUntilCanceledThenReleased(
        CancellationToken cancellationToken,
        TaskCompletionSource started,
        TaskCompletionSource cancellationObserved,
        TaskCompletionSource release)
    {
        started.TrySetResult();
        await ObserveCancellationAndWaitToFinish(cancellationToken, cancellationObserved, release);
    }

    private static async Task ObserveCancellationAndWaitToFinish(
        CancellationToken cancellationToken,
        TaskCompletionSource cancellationObserved,
        TaskCompletionSource release)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationObserved.TrySetResult();
        }

        await release.Task;
    }
}
