using System.Diagnostics;
using System.Net;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Testing.Headless;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public sealed class PackageInstallerDisposeTests
{
    private readonly List<(BeutlApiApplication App, HttpClient HttpClient)> _applications = [];

    [SetUp]
    public void SetUp()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach ((BeutlApiApplication app, HttpClient httpClient) in _applications)
        {
            await app.DisposeAsync();
            httpClient.Dispose();
        }

        _applications.Clear();
    }

    [Test]
    public async Task DisposeAsync_DrainsInFlightOperations_BeforeDisposingResources()
    {
        var handler = new BlockingHandler();
        var installer = CreateInstaller(handler);

        var context = new PackageInstallContext(
            "Beutl.Package.DisposeTest", "1.0.0", "https://example.com/package.nupkg");
        Task download = installer.DownloadPackageFile(context, cancellationToken: CancellationToken.None);

        await handler.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        Task dispose = installer.DisposeAsync().AsTask();
        // DisposeAsync reaches its first incomplete await before returning. The handler is
        // already blocked, so no wall-clock delay is needed to observe the drain wait.
        try
        {
            Assert.That(dispose.IsCompleted, Is.False, "disposal must wait for the in-flight download");
            Assert.That(handler.Disposed, Is.False);
        }
        finally
        {
            handler.Release();
        }
        await dispose.WaitAsync(TimeSpan.FromSeconds(10));
        await download.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(handler.Disposed, Is.True, "the owned HttpClient must be disposed after draining");
    }

    [Test]
    public async Task DisposeAsync_DrainsCompositeInstallOperations()
    {
        using var handler = new BlockingHandler();
        handler.Release();
        var installer = CreateInstaller(handler);

        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task operation = installer.TrackInstallOperationAsync(async () =>
        {
            operationStarted.TrySetResult();
            await releaseOperation.Task;
        });
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task dispose = installer.DisposeAsync().AsTask();
        try
        {
            Assert.That(dispose.IsCompleted, Is.False, "disposal must wait for the composite operation");
        }
        finally
        {
            releaseOperation.TrySetResult();
        }
        await Task.WhenAll(operation, dispose).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task DisposeAsync_AllowsNestedPhases_OfAnAdmittedTransaction()
    {
        using var handler = new BlockingHandler();
        handler.Release();
        var installer = CreateInstaller(handler);

        var phaseStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePhase = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool nestedPhaseRan = false;
        Task operation = installer.TrackInstallOperationAsync(async () =>
        {
            phaseStarted.TrySetResult();
            await releasePhase.Task;
            // Disposal has started by now; a nested phase must still be admitted.
            await installer.DownloadPackageFile(
                new PackageInstallContext(
                    "Beutl.Package.NestedPhase", "1.0.0", "https://example.com/package.nupkg"),
                cancellationToken: CancellationToken.None);
            nestedPhaseRan = true;
        });
        await phaseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task dispose = installer.DisposeAsync().AsTask();
        releasePhase.TrySetResult();

        await Task.WhenAll(operation, dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(nestedPhaseRan, Is.True, "the nested phase must run while the transaction is drained");
    }

    [Test]
    public async Task DisposeAsync_ReentrantFromCallback_DrainsTheTransaction()
    {
        var installer = CreateInstaller();

        Task? dispose = null;
        Task operation = installer.TrackInstallOperationAsync(async () =>
        {
            // A re-entrant DisposeAsync must drain this transaction.
            dispose = installer.DisposeAsync().AsTask();
            await Task.Delay(1).ConfigureAwait(false);
        });

        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        await dispose!.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task TrackInstallOperationAsync_PropagatesFaultedTransaction()
    {
        var installer = CreateInstaller();

        Task operation = installer.TrackInstallOperationAsync(async () =>
        {
            await Task.Delay(1).ConfigureAwait(false);
            throw new InvalidOperationException("install failed");
        });

        await Assert.CatchAsync<InvalidOperationException>(async () =>
            await operation.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task DisposeAsync_DrainsPhaseRegisteredWhileDisposalIsRunning()
    {
        var handler = new BlockingHandler();
        var installer = CreateInstaller(handler);

        var phaseStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePhase = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task phase = installer.DownloadPackageFile(
            new PackageInstallContext("Beutl.Package.RacePhase", "1.0.0", "https://example.com/package.nupkg"),
            cancellationToken: CancellationToken.None);
        // The phase is registered before the network call; disposal must drain it.
        Task dispose = installer.DisposeAsync().AsTask();
        await handler.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(dispose.IsCompleted, Is.False,
            "disposal must wait for the phase that was admitted while disposal started");

        handler.Release();
        await Task.WhenAll(phase, dispose).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task TrackedGenericPhase_PreservesCancellationState()
    {
        using var handler = new BlockingHandler();
        handler.Release();
        var installer = CreateInstaller(out BeutlApiApplication app, handler);

        var owner = new Profile(ApiTestAuthentication.CreateProfileResponse(), app);
        var package = new Package(owner, CreatePackageResponse(), app);
        var release = new Release(
            package,
            new ReleaseResponse
            {
                Id = "release-id",
                Version = "1.0.0",
                Title = "Release",
                Description = "Description",
                TargetVersion = null,
                FileId = null,
                FileUrl = null,
            },
            app);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Task<PackageInstallContext> phase = installer.PrepareForInstall(release, cancellationToken: cancellation.Token);

        Assert.That(phase.IsCanceled, Is.True,
            "a canceled tracked phase must surface as a canceled task, not a faulted one");
    }

    [Test]
    public async Task DisposeAsync_StopsWaitingAtTheDrainDeadline_WhenAnOperationNeverCompletes()
    {
        var installer = CreateInstaller(shortDeadline: true);

        // Keep the operation pending through the deadline, then release it during cleanup.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task operation = installer.TrackInstallOperationAsync(() => release.Task);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            Task dispose = installer.DisposeAsync().AsTask();
            Assert.That(dispose.IsCompleted, Is.False, "disposal must first wait for the drain deadline");
            TimeSpan completedAfter = await CaptureCompletionTime(dispose, stopwatch)
                .WaitAsync(TimeSpan.FromSeconds(10));
            stopwatch.Stop();

            Assert.That(completedAfter, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100)),
                "disposal must remain pending for the initial part of its 500 ms drain deadline");
            Assert.That(operation.IsCompleted, Is.False);
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)),
                "disposal must stop waiting at the drain deadline even when an operation never completes");
        }
        finally
        {
            release.TrySetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task DisposeAsync_DrainsSynchronousOperations_UntilCompletion()
    {
        var installer = CreateInstaller();

        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool operationCompleted = false;

        Task syncOperation = Task.Run(() =>
            installer.TrackSyncOperation(() =>
            {
                operationStarted.TrySetResult();
                releaseOperation.Task.Wait();
                operationCompleted = true;
            }));

        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task dispose = installer.DisposeAsync().AsTask();
        try
        {
            Assert.That(dispose.IsCompleted, Is.False,
                "disposal must wait for the admitted synchronous operation");
        }
        finally
        {
            releaseOperation.TrySetResult();
        }
        await Task.WhenAll(syncOperation, dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(operationCompleted, Is.True);
    }

    [Test]
    public async Task DisposeAsync_AdmitsNestedSynchronousPhases_OfAnAdmittedTransaction()
    {
        var installer = CreateInstaller();

        var phaseStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePhase = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool nestedPhaseRan = false;
        Task operation = installer.TrackInstallOperationAsync(async () =>
        {
            phaseStarted.TrySetResult();
            await releasePhase.Task;
            // Disposal has started by now; a nested synchronous phase must still be admitted.
            installer.TrackSyncOperation(() => nestedPhaseRan = true);
        });
        await phaseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task dispose = installer.DisposeAsync().AsTask();
        releasePhase.TrySetResult();

        await Task.WhenAll(operation, dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(nestedPhaseRan, Is.True,
            "the nested synchronous phase must run while the transaction is drained");
    }

    [Test]
    public async Task DisposeAsync_CompletesNormally_WhenASynchronousOperationThrows()
    {
        var installer = CreateInstaller();

        // The lifetime proxy must not fault when the operation throws: the caller observes
        // the exception directly, and a faulted proxy would surface later as an unobserved
        // task exception when the drain loop removes it.
        Assert.Throws<InvalidOperationException>(() =>
            installer.TrackSyncOperation(() => throw new InvalidOperationException("sync phase failed")));

        await installer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task PrepareForInstall_StringOverload_RejectsAdmissionAfterDisposal()
    {
        var installer = CreateInstaller();
        await installer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        // The string-based overload must be admitted like every other synchronous entry
        // point, so a cached installer cannot mint a context after disposal completed.
        Assert.Throws<ObjectDisposedException>(() =>
            installer.PrepareForInstall("Beutl.Package.PostDisposal", "1.0.0"));
    }

    [Test]
    public async Task DisposeAsync_StartsDraining_WhenTheTransactionBlocksBeforeItsFirstAwait()
    {
        var installer = CreateInstaller(shortDeadline: true);

        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The delegate blocks synchronously before its first incomplete await, so the
        // admission call itself blocks; run it on a worker so disposal can be observed.
        Task operation = Task.Run(() =>
            installer.TrackInstallOperationAsync(async () =>
            {
                operationStarted.TrySetResult();
                releaseOperation.Task.Wait();
                await Task.CompletedTask;
            }));
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task dispose = installer.DisposeAsync().AsTask();
        try
        {
            Assert.That(dispose.IsCompleted, Is.False,
                "disposal must start draining while the transaction blocks before its first await");
        }
        finally
        {
            releaseOperation.TrySetResult();
        }
        await Task.WhenAll(operation, dispose).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task DisposeAsync_KeepsInstallerResourcesAlive_UntilTrackedWorkStops()
    {
        using var handler = new BlockingHandler();
        var installer = CreateInstaller(handler, shortDeadline: true);

        Task operation = installer.DownloadPackageFile(
            new PackageInstallContext(
                "Beutl.Package.KeepAlive", "1.0.0", "https://example.com/package.nupkg"),
            cancellationToken: CancellationToken.None);

        await handler.Entered.WaitAsync(TimeSpan.FromSeconds(5));

        // Disposal stops waiting at its deadline, but the installer resources must
        // not be released while the tracked operation is still running.
        Task dispose = installer.DisposeAsync().AsTask();
        await dispose.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(handler.Disposed, Is.False,
            "the installer must keep its HttpClient alive while a tracked operation is still running");

        // Once the tracked work stops, the resource teardown must run to completion.
        handler.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        await TestWait.UntilAsync(() => handler.Disposed, TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task WaitUntilIdleAsync_WaitsForOperationsThatOutlivedTheDrainDeadline()
    {
        var installer = CreateInstaller(shortDeadline: true);

        // An operation that never completes, so disposal ends at its deadline.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task operation = installer.TrackInstallOperationAsync(async () => await release.Task);

        await installer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        Task idle = installer.WaitUntilIdleAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.That(idle.IsCompleted, Is.False,
                "waiting for idleness must not complete while an operation is still running");
        }
        finally
        {
            release.TrySetResult();
        }
        await Task.WhenAll(idle, operation).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task WaitUntilIdleAsync_StopsAtTheTimeout_WhenAnOperationNeverCompletes()
    {
        var installer = CreateInstaller();

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task operation = installer.TrackInstallOperationAsync(() => release.Task);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            Task idle = installer.WaitUntilIdleAsync(TimeSpan.FromMilliseconds(300));
            Assert.That(idle.IsCompleted, Is.False, "the idle wait must first wait for its timeout");
            TimeSpan completedAfter = await CaptureCompletionTime(idle, stopwatch)
                .WaitAsync(TimeSpan.FromSeconds(5));
            stopwatch.Stop();

            Assert.That(completedAfter, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100)),
                "the idle wait must remain pending for the initial part of its 300 ms timeout");
            Assert.That(operation.IsCompleted, Is.False);
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
        }
        finally
        {
            release.TrySetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            await installer.DisposeAsync();
        }
    }

    [Test]
    public async Task WaitUntilIdleAsync_PublishesFallbackBeforeReturningAtTheDeadline()
    {
        var installer = CreateInstaller(ownsHttpClient: false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int fallbackCount = 0;
        Task operation = installer.TrackInstallOperationWithShutdownFallbackAsync(
            () => release.Task,
            () => Interlocked.Increment(ref fallbackCount));

        await installer.WaitUntilIdleAsync(TimeSpan.FromMilliseconds(100));
        await installer.WaitUntilIdleAsync(TimeSpan.FromMilliseconds(50));

        Assert.That(fallbackCount, Is.EqualTo(1));

        release.TrySetResult();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        await installer.DisposeAsync();
    }

    [Test]
    public async Task SuccessfulInstallDisarmsFallbackBeforeShutdownSnapshot()
    {
        var installer = CreateInstaller(ownsHttpClient: false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fallbackDisarmed = new ManualResetEventSlim();
        int fallbackCount = 0;
        Task operation = installer.TrackInstallOperationWithShutdownFallbackAsync(
            () => release.Task,
            () => Interlocked.Increment(ref fallbackCount));
        installer.AfterSuccessfulInstallFallbackDisarmed = fallbackDisarmed.Set;
        installer.BeforeShutdownFallbackSnapshot = () =>
        {
            release.TrySetResult();
            Assert.That(fallbackDisarmed.Wait(TimeSpan.FromSeconds(5)), Is.True);
        };
        installer.BeginShutdown();

        await installer.WaitUntilIdleAsync(TimeSpan.Zero)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(fallbackCount, Is.Zero);
        await installer.DisposeAsync();
    }

    [Test]
    public async Task FaultedInstallPublishesFallbackBeforeItsTaskCompletes()
    {
        var installer = CreateInstaller(ownsHttpClient: false);
        int fallbackCount = 0;

        Task operation = installer.TrackInstallOperationWithShutdownFallbackAsync(
            () => Task.FromException(new InvalidOperationException("install failed")),
            () => Interlocked.Increment(ref fallbackCount));

        await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
        Assert.That(fallbackCount, Is.EqualTo(1));
        await installer.DisposeAsync();
    }

    [Test]
    public async Task NormalInstallCancellationDoesNotPublishShutdownFallback()
    {
        var installer = CreateInstaller(ownsHttpClient: false);
        int fallbackCount = 0;

        Task operation = installer.TrackInstallOperationWithShutdownFallbackAsync(
            () => Task.FromCanceled(new CancellationToken(canceled: true)),
            () => Interlocked.Increment(ref fallbackCount));

        await Assert.CatchAsync<OperationCanceledException>(() => operation);
        Assert.That(fallbackCount, Is.Zero);
        await installer.DisposeAsync();
    }

    [Test]
    public async Task InstallCancellationAfterShutdownStartsButBeforeIdleWaitPublishesFallback()
    {
        var installer = CreateInstaller(ownsHttpClient: false);
        var cancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int fallbackCount = 0;
        Task operation = installer.TrackInstallOperationWithShutdownFallbackAsync(
            () => cancellation.Task,
            () => Interlocked.Increment(ref fallbackCount));
        installer.BeginShutdown();

        cancellation.TrySetCanceled();
        await Assert.CatchAsync<OperationCanceledException>(() => operation);
        await installer.WaitUntilIdleAsync(TimeSpan.FromSeconds(1));

        Assert.That(fallbackCount, Is.EqualTo(1));
        await installer.DisposeAsync();
    }

    // Observe completion before the awaiting test continuation can be delayed. The assertions use
    // a conservative 100 ms minimum, well below both deadlines, so timer rounding cannot fail them.
    // A second Task.Delay would race the real deadline when a busy timer queue drains both at once.
    private static Task<TimeSpan> CaptureCompletionTime(Task task, Stopwatch stopwatch)
        => task.ContinueWith(completed =>
        {
            TimeSpan elapsed = stopwatch.Elapsed;
            completed.GetAwaiter().GetResult();
            return elapsed;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private PackageInstaller CreateInstaller(
        HttpMessageHandler? handler = null,
        bool ownsHttpClient = true,
        bool shortDeadline = false)
        => CreateInstaller(out _, handler, ownsHttpClient, shortDeadline);

    private PackageInstaller CreateInstaller(
        out BeutlApiApplication app,
        HttpMessageHandler? handler = null,
        bool ownsHttpClient = true,
        bool shortDeadline = false)
    {
        HttpClient httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        _applications.Add((app, httpClient));
        var repository = new InstalledPackageRepository();
        return shortDeadline
            ? new ShortDeadlinePackageInstaller(httpClient, ownsHttpClient, repository, app)
            : new PackageInstaller(httpClient, ownsHttpClient, repository, app);
    }

    private sealed class ShortDeadlinePackageInstaller : PackageInstaller
    {
        public ShortDeadlinePackageInstaller(
            HttpClient httpClient,
            bool ownsHttpClient,
            InstalledPackageRepository installedPackageRepository,
            BeutlApiApplication apiApplication)
            : base(httpClient, ownsHttpClient, installedPackageRepository, apiApplication)
        {
        }

        protected override long DrainDeadlineMilliseconds => 500;
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public bool Disposed { get; private set; }

        public void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("package content")
            };
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Disposed = true;
        }
    }

    private static PackageResponse CreatePackageResponse()
    {
        return new PackageResponse
        {
            Id = "package-id",
            Owner = ApiTestAuthentication.CreateProfileResponse(),
            Name = "package-name",
            DisplayName = "Package Name",
            Description = "Description",
            ShortDescription = "Short description",
            WebSite = "",
            Tags = [],
            LogoId = null,
            LogoUrl = null,
            Screenshots = [],
            Currency = null,
            Price = null,
            Paid = false,
            Owned = false,
        };
    }
}
