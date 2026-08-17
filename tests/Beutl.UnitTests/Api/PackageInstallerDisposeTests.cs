using System.Diagnostics;
using System.Net;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Testing.Headless;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public sealed class PackageInstallerDisposeTests
{
    [Test]
    public async Task DisposeAsync_DrainsInFlightOperations_BeforeDisposingResources()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        var handler = new BlockingHandler();
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new PackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);

        var context = new PackageInstallContext(
            "Beutl.Package.DisposeTest", "1.0.0", "https://example.com/package.nupkg");
        Task download = installer.DownloadPackageFile(context, cancellationToken: CancellationToken.None);

        await handler.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        Task dispose = installer.DisposeAsync().AsTask();
        await Task.Delay(500);
        Assert.That(dispose.IsCompleted, Is.False, "disposal must wait for the in-flight download");

        handler.Release();
        await dispose.WaitAsync(TimeSpan.FromSeconds(10));
        await download.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(handler.Disposed, Is.True, "the owned HttpClient must be disposed after draining");
    }

    [Test]
    public async Task DisposeAsync_DrainsCompositeInstallOperations()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        using var handler = new BlockingHandler();
        handler.Release();
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new PackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);

        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task operation = installer.TrackInstallOperationAsync(async () =>
        {
            operationStarted.TrySetResult();
            await releaseOperation.Task;
        });
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task dispose = installer.DisposeAsync().AsTask();
        await Task.Delay(300);
        Assert.That(dispose.IsCompleted, Is.False, "disposal must wait for the composite operation");

        releaseOperation.TrySetResult();
        await Task.WhenAll(operation, dispose).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task DisposeAsync_AllowsNestedPhases_OfAnAdmittedTransaction()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new PackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);

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
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new PackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);

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
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new PackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);

        Task operation = installer.TrackInstallOperationAsync(async () =>
        {
            await Task.Delay(1).ConfigureAwait(false);
            throw new InvalidOperationException("install failed");
        });

        Assert.CatchAsync<InvalidOperationException>(async () =>
            await operation.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task DisposeAsync_DrainsPhaseRegisteredWhileDisposalIsRunning()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        var handler = new BlockingHandler();
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new PackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);

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
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        using var handler = new BlockingHandler();
        handler.Release();
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new PackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);

        var owner = new Profile(CreateProfileResponse(), app);
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
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new ShortDeadlinePackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);

        // A tracked operation that never completes must not outlive the drain deadline.
        Task operation = installer.TrackInstallOperationAsync(async () =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan);
        });

        var stopwatch = Stopwatch.StartNew();
        await installer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        stopwatch.Stop();

        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)),
            "disposal must stop waiting at the drain deadline even when an operation never completes");
    }

    [Test]
    public async Task DisposeAsync_DrainsSynchronousOperations_UntilCompletion()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new PackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);

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
        await Task.Delay(300);
        Assert.That(dispose.IsCompleted, Is.False,
            "disposal must wait for the admitted synchronous operation");

        releaseOperation.TrySetResult();
        await Task.WhenAll(syncOperation, dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(operationCompleted, Is.True);
    }

    [Test]
    public async Task DisposeAsync_AdmitsNestedSynchronousPhases_OfAnAdmittedTransaction()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new PackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);

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
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new PackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);

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
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new PackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);
        await installer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        // The string-based overload must be admitted like every other synchronous entry
        // point, so a cached installer cannot mint a context after disposal completed.
        Assert.Throws<ObjectDisposedException>(() =>
            installer.PrepareForInstall("Beutl.Package.PostDisposal", "1.0.0"));
    }

    [Test]
    public async Task DisposeAsync_StartsDraining_WhenTheTransactionBlocksBeforeItsFirstAwait()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new ShortDeadlinePackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);

        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The delegate blocks synchronously before its first incomplete await, so the
        // admission call itself blocks; run it on a worker so disposal can be observed.
        Task operation = Task.Run(() =>
            installer.TrackInstallOperationAsync(async () =>
            {
                // Block synchronously before the first incomplete await: the transaction
                // runner must not hold the gate, or disposal could not even start draining.
                operationStarted.TrySetResult();
                releaseOperation.Task.Wait();
                await Task.CompletedTask;
            }));
        await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Disposal must be able to acquire the gate and start its drain deadline even
        // though the transaction is still blocked in its synchronous prefix.
        Task dispose = installer.DisposeAsync().AsTask();
        await Task.Delay(300);
        Assert.That(dispose.IsCompleted, Is.False,
            "disposal must start draining while the transaction blocks before its first await");

        releaseOperation.TrySetResult();
        await Task.WhenAll(operation, dispose).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task DisposeAsync_KeepsInstallerResourcesAlive_UntilTrackedWorkStops()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));

        using var handler = new BlockingHandler();
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var installer = new ShortDeadlinePackageInstaller(
            httpClient,
            ownsHttpClient: true,
            new InstalledPackageRepository(),
            app);

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

        await WaitUntilAsync(() => handler.Disposed, TimeSpan.FromSeconds(10));
        Assert.That(handler.Disposed, Is.True,
            "the installer must dispose its HttpClient after the tracked operation stopped");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10);
        }

        Assert.That(condition(), Is.True, "condition did not become true within the timeout");
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

    private static ProfileResponse CreateProfileResponse()
    {
        return new ProfileResponse
        {
            Id = "profile-id",
            Name = "profile-name",
            DisplayName = "Profile Name",
            Bio = null,
            IconId = null,
            IconUrl = null,
        };
    }

    private static PackageResponse CreatePackageResponse()
    {
        return new PackageResponse
        {
            Id = "package-id",
            Owner = CreateProfileResponse(),
            Name = "package-name",
            DisplayName = "Package Name",
            Description = "Description",
            ShortDescription = "Short description",
            WebSite = null,
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
