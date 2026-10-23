using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.Services;

namespace Beutl.HeadlessUITests;

// ProxyMediaServices rebuilds its backing store/eviction when StoreRootPath or MaxTotalBytes changes at
// runtime, and hands editors swap-stable facades so a cached reference keeps hitting the current store.
[TestFixture]
[NonParallelizable] // drives the ProxyMediaServices.Current singleton + GlobalConfiguration.Instance
public sealed class ProxyMediaServicesReinitTests
{
    private static (string Root, long Cap) Snapshot(ProxyStoreConfig config) => (config.StoreRootPath.Value, config.MaxTotalBytes.Value);

    private static void Restore(ProxyStoreConfig config, (string Root, long Cap) prior)
    {
        config.StoreRootPath.Value = prior.Root;
        config.MaxTotalBytes.Value = prior.Cap;
    }

    [AvaloniaTest]
    public void StoreRootPath_change_rebuilds_store_behind_the_stable_facade()
    {
        using var fixture = ProxyServicesFixture.Start();
        ProxyMediaServices services = fixture.Services;
        IProxyStore facade = services.StoreFacade;
        Assert.That(facade.StoreRootPath, Is.EqualTo(Path.GetFullPath(fixture.First)));

        fixture.Config.StoreRootPath.Value = fixture.Second;

        Assert.Multiple(() =>
        {
            Assert.That(services.StoreFacade, Is.SameAs(facade));
            Assert.That(facade.StoreRootPath, Is.EqualTo(Path.GetFullPath(fixture.Second)));
            Assert.That(services.Store.StoreRootPath, Is.EqualTo(Path.GetFullPath(fixture.Second)));
        });
    }

    [AvaloniaTest]
    public void MaxTotalBytes_change_rebuilds_eviction_cap()
    {
        // MaxTotalBytes clamps to [MinTotalBytes (5 GiB), MaxTotalBytesLimit]; pick values above the floor.
        using var fixture = ProxyServicesFixture.Start(maxTotalBytes: 8L * 1024 * 1024 * 1024);
        ProxyMediaServices services = fixture.Services;
        IProxyStoreCapInfo capFacade = services.CapInfoFacade;
        Assert.That(capFacade.MaxTotalBytes, Is.EqualTo(8L * 1024 * 1024 * 1024));
        // A cap-only change must not tear down the store/queue (that would cancel in-flight jobs).
        ProxyStore storeBefore = services.Store;
        ProxyJobQueue queueBefore = services.Queue;

        fixture.Config.MaxTotalBytes.Value = 16L * 1024 * 1024 * 1024;

        Assert.Multiple(() =>
        {
            Assert.That(services.CapInfoFacade, Is.SameAs(capFacade));
            Assert.That(capFacade.MaxTotalBytes, Is.EqualTo(16L * 1024 * 1024 * 1024));
            Assert.That(services.StoreFacade.StoreRootPath, Is.EqualTo(Path.GetFullPath(fixture.First)));
            Assert.That(services.Store, Is.SameAs(storeBefore));
            Assert.That(services.Queue, Is.SameAs(queueBefore));
        });
    }

    [AvaloniaTest]
    public void StoreRootPath_change_bumps_resolver_source_version()
    {
        using var fixture = ProxyServicesFixture.Start();
        ProxyMediaServices services = fixture.Services;
        // A reader keys reuse on GetSourceVersion; a startup-index entry reports 0 on both stores, so
        // without a version bump the swap would go unnoticed. The rebuilt resolver must report a
        // strictly larger version for the same source so the open reader reopens against the new store.
        string sourceKey = Path.Combine(fixture.First, "clip.mov");
        long versionBefore = services.ResolverFacade.GetSourceVersion(sourceKey);

        fixture.Config.StoreRootPath.Value = fixture.Second;

        long versionAfter = services.ResolverFacade.GetSourceVersion(sourceKey);
        Assert.That(versionAfter, Is.GreaterThan(versionBefore));
    }

    [AvaloniaTest]
    public void Store_facade_forwards_Changed_events_across_a_rebuild()
    {
        using var fixture = ProxyServicesFixture.Start();
        ProxyMediaServices services = fixture.Services;
        int changedCount = 0;
        void Handler(object? sender, ProxyStoreChangedEventArgs e) => changedCount++;
        fixture.OnStoreChanged(Handler);

        fixture.Config.StoreRootPath.Value = fixture.Second;

        // A subscriber bound to the facade before the rebuild must receive events from the new store.
        var fingerprint = new ProxyFingerprint(Path.Combine(fixture.Second, "source.mov"), 1, DateTime.UtcNow);
        services.Store.Register(new ProxyEntry(
            fingerprint, ProxyPreset.Quarter, ProxyState.Generating, $"proxy/{Guid.NewGuid():N}.mp4", 0,
            new PixelSize(100, 100), default, DateTime.UtcNow, DateTime.UtcNow, null));

        Assert.That(changedCount, Is.GreaterThan(0));
    }

    [AvaloniaTest]
    public void Store_facade_raises_Reset_on_store_swap()
    {
        using var fixture = ProxyServicesFixture.Start();
        int resetCount = 0;
        void Handler(object? sender, ProxyStoreChangedEventArgs e)
        {
            if (e.Kind == ProxyStoreChangeKind.Reset)
                resetCount++;
        }

        fixture.OnStoreChanged(Handler);

        // The new store loads its index in the constructor (no Registered events), so a swap must
        // raise a Reset for open Proxies tabs / badges to refresh to the new store's state.
        fixture.Config.StoreRootPath.Value = fixture.Second;

        Assert.That(resetCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void StoreRootPath_caseOnlyChange_doesNotRebuild()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            Assert.Ignore("Case-insensitive path comparison only applies on Windows/macOS.");
        }

        using var fixture = ProxyServicesFixture.Start();
        ProxyMediaServices services = fixture.Services;
        ProxyStore storeBefore = services.Store;
        ProxyJobQueue queueBefore = services.Queue;

        // Same directory, different case: on Windows/macOS this is the same filesystem location, so
        // the store/queue must not be torn down (which would cancel in-flight jobs).
        fixture.Config.StoreRootPath.Value = fixture.First.ToUpperInvariant();

        Assert.Multiple(() =>
        {
            Assert.That(services.Store, Is.SameAs(storeBefore));
            Assert.That(services.Queue, Is.SameAs(queueBefore));
        });
    }

    [AvaloniaTest]
    public void Store_reset_fires_after_resolver_and_queue_facades_repoint()
    {
        using var fixture = ProxyServicesFixture.Start();
        ProxyMediaServices services = fixture.Services;
        string sourceKey = Path.Combine(fixture.First, "clip.mov");
        long versionBefore = services.ResolverFacade.GetSourceVersion(sourceKey);
        long versionAtReset = long.MinValue;
        void Handler(object? sender, ProxyStoreChangedEventArgs e)
        {
            // A Reset handler (badge/filmstrip refresh) recomputes through the Queue/Resolver facades, so
            // the store swap must fire Reset only after those repoint. If Reset fired first, the resolver
            // facade would still serve the old store's version here.
            if (e.Kind == ProxyStoreChangeKind.Reset)
                versionAtReset = services.ResolverFacade.GetSourceVersion(sourceKey);
        }

        fixture.OnStoreChanged(Handler);

        fixture.Config.StoreRootPath.Value = fixture.Second;

        Assert.That(versionAtReset, Is.GreaterThan(versionBefore));
    }

    [AvaloniaTest]
    public void StoreRootPath_invalidPath_keepsPreviousStore_andDoesNotThrow()
    {
        using var fixture = ProxyServicesFixture.Start();
        ProxyMediaServices services = fixture.Services;
        string good = fixture.First;
        // An existing file cannot be opened as a store directory: ProxyStore's Directory.CreateDirectory
        // throws, and the rebuild must swallow that and keep the live store rather than escaping the setter.
        string badFile = fixture.Second;
        File.WriteAllBytes(badFile, [1]);
        ProxyStore storeBefore = services.Store;

        Assert.DoesNotThrow(() => fixture.Config.StoreRootPath.Value = badFile);

        Assert.Multiple(() =>
        {
            Assert.That(services.Store, Is.SameAs(storeBefore));
            Assert.That(services.Store.StoreRootPath, Is.EqualTo(Path.GetFullPath(good)));
            Assert.That(services.StoreFacade.StoreRootPath, Is.EqualTo(Path.GetFullPath(good)));
            // The rejected path must be reverted so GlobalConfiguration's auto-save does not persist it.
            Assert.That(fixture.Config.StoreRootPath.Value, Is.EqualTo(Path.GetFullPath(good)));
        });
    }

    [AvaloniaTest]
    public void Initialize_invalidPersistedPath_fallsBackToDefaultStore()
    {
        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        (string Root, long Cap) prior = Snapshot(config);
        // A bad store path persisted to settings.json from a previous session: startup must still bring up
        // proxy services (on the default location) rather than leaving Current null until another restart.
        string badFile = Path.Combine(Path.GetTempPath(), $"proxy-init-file-{Guid.NewGuid():N}");
        File.WriteAllBytes(badFile, [1]);
        config.StoreRootPath.Value = badFile;

        ProxyMediaServices services = null!;
        try
        {
            Assert.DoesNotThrow(() => services = ProxyMediaServices.Initialize(GlobalConfiguration.Instance));

            string defaultRoot = Path.GetFullPath(ProxyStoreConfig.DefaultStoreRootPath);
            Assert.Multiple(() =>
            {
                Assert.That(ProxyMediaServices.Current, Is.Not.Null);
                Assert.That(services.Store.StoreRootPath, Is.EqualTo(defaultRoot));
                Assert.That(config.StoreRootPath.Value, Is.EqualTo(defaultRoot), "the bad persisted path must be repaired to the default");
            });
        }
        finally
        {
            services?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Restore(config, prior);
            try { File.Delete(badFile); } catch { /* best-effort test cleanup */ }
        }
    }

    [AvaloniaTest]
    public void Store_swap_completes_even_if_a_Reset_subscriber_throws()
    {
        using var fixture = ProxyServicesFixture.Start();
        ProxyMediaServices services = fixture.Services;
        string sourceKey = Path.Combine(fixture.First, "clip.mov");
        long versionBefore = services.ResolverFacade.GetSourceVersion(sourceKey);
        void Throwing(object? sender, ProxyStoreChangedEventArgs e) => throw new InvalidOperationException("boom");
        fixture.OnStoreChanged(Throwing);

        // A throwing Reset subscriber must not abort the swap.
        fixture.Config.StoreRootPath.Value = fixture.Second;

        Assert.Multiple(() =>
        {
            Assert.That(services.Store.StoreRootPath, Is.EqualTo(Path.GetFullPath(fixture.Second)));
            Assert.That(services.ResolverFacade.GetSourceVersion(sourceKey), Is.GreaterThan(versionBefore));
        });
    }

    // Points the proxy store at the new temp folder First and starts the services. Dispose unsubscribes
    // the store handlers, stops the services, restores the configuration and deletes First and Second.
    private sealed class ProxyServicesFixture : IDisposable
    {
        private readonly (string Root, long Cap) _prior;
        private readonly List<EventHandler<ProxyStoreChangedEventArgs>> _storeHandlers = [];

        private ProxyServicesFixture(long? maxTotalBytes)
        {
            _prior = Snapshot(Config);
            Config.StoreRootPath.Value = First;
            if (maxTotalBytes is { } cap)
                Config.MaxTotalBytes.Value = cap;

            Services = ProxyMediaServices.Initialize(GlobalConfiguration.Instance);
        }

        public ProxyStoreConfig Config { get; } = GlobalConfiguration.Instance.ProxyStoreConfig;

        public string First { get; } = NewTempPath();

        public string Second { get; } = NewTempPath();

        public ProxyMediaServices Services { get; }

        public static ProxyServicesFixture Start(long? maxTotalBytes = null) => new(maxTotalBytes);

        public void OnStoreChanged(EventHandler<ProxyStoreChangedEventArgs> handler)
        {
            Services.StoreFacade.Changed += handler;
            _storeHandlers.Add(handler);
        }

        public void Dispose()
        {
            foreach (EventHandler<ProxyStoreChangedEventArgs> handler in _storeHandlers)
                Services.StoreFacade.Changed -= handler;

            Services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Restore(Config, _prior);
            TryDelete(First);
            TryDelete(Second);
        }

        private static string NewTempPath() => Path.Combine(Path.GetTempPath(), $"proxy-reinit-{Guid.NewGuid():N}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else
                File.Delete(path);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }
}
