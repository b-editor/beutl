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
    private static (string Root, long Cap) Snapshot(ProxyStoreConfig config) => (config.StoreRootPath, config.MaxTotalBytes);

    private static void Restore(ProxyStoreConfig config, (string Root, long Cap) prior)
    {
        config.StoreRootPath = prior.Root;
        config.MaxTotalBytes = prior.Cap;
    }

    [AvaloniaTest]
    public void StoreRootPath_change_rebuilds_store_behind_the_stable_facade()
    {
        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        (string Root, long Cap) prior = Snapshot(config);
        string first = Path.Combine(Path.GetTempPath(), $"proxy-reinit-{Guid.NewGuid():N}");
        string second = Path.Combine(Path.GetTempPath(), $"proxy-reinit-{Guid.NewGuid():N}");
        config.StoreRootPath = first;

        ProxyMediaServices services = ProxyMediaServices.Initialize(GlobalConfiguration.Instance);
        try
        {
            IProxyStore facade = services.StoreFacade;
            Assert.That(facade.StoreRootPath, Is.EqualTo(Path.GetFullPath(first)));

            config.StoreRootPath = second;

            Assert.Multiple(() =>
            {
                // Same facade instance, now pointing at the rebuilt store on the new path.
                Assert.That(services.StoreFacade, Is.SameAs(facade));
                Assert.That(facade.StoreRootPath, Is.EqualTo(Path.GetFullPath(second)));
                Assert.That(services.Store.StoreRootPath, Is.EqualTo(Path.GetFullPath(second)));
            });
        }
        finally
        {
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Restore(config, prior);
            TryDelete(first);
            TryDelete(second);
        }
    }

    [AvaloniaTest]
    public void MaxTotalBytes_change_rebuilds_eviction_cap()
    {
        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        (string Root, long Cap) prior = Snapshot(config);
        string root = Path.Combine(Path.GetTempPath(), $"proxy-reinit-{Guid.NewGuid():N}");
        config.StoreRootPath = root;
        // MaxTotalBytes clamps to [MinTotalBytes (5 GiB), MaxTotalBytesLimit]; pick values above the floor.
        config.MaxTotalBytes = 8L * 1024 * 1024 * 1024;

        ProxyMediaServices services = ProxyMediaServices.Initialize(GlobalConfiguration.Instance);
        try
        {
            IProxyStoreCapInfo capFacade = services.CapInfoFacade;
            Assert.That(capFacade.MaxTotalBytes, Is.EqualTo(8L * 1024 * 1024 * 1024));
            // A cap-only change must not tear down the store/queue (that would cancel in-flight jobs).
            ProxyStore storeBefore = services.Store;
            ProxyJobQueue queueBefore = services.Queue;

            config.MaxTotalBytes = 16L * 1024 * 1024 * 1024;

            Assert.Multiple(() =>
            {
                Assert.That(services.CapInfoFacade, Is.SameAs(capFacade));
                Assert.That(capFacade.MaxTotalBytes, Is.EqualTo(16L * 1024 * 1024 * 1024));
                // The store root did not change, so the facade must keep serving that same path.
                Assert.That(services.StoreFacade.StoreRootPath, Is.EqualTo(Path.GetFullPath(root)));
                // Same live store and queue — the cap change retargeted eviction in place.
                Assert.That(services.Store, Is.SameAs(storeBefore));
                Assert.That(services.Queue, Is.SameAs(queueBefore));
            });
        }
        finally
        {
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Restore(config, prior);
            TryDelete(root);
        }
    }

    [AvaloniaTest]
    public void StoreRootPath_change_bumps_resolver_source_version()
    {
        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        (string Root, long Cap) prior = Snapshot(config);
        string first = Path.Combine(Path.GetTempPath(), $"proxy-reinit-{Guid.NewGuid():N}");
        string second = Path.Combine(Path.GetTempPath(), $"proxy-reinit-{Guid.NewGuid():N}");
        config.StoreRootPath = first;

        ProxyMediaServices services = ProxyMediaServices.Initialize(GlobalConfiguration.Instance);
        try
        {
            // A reader keys reuse on GetSourceVersion; a startup-index entry reports 0 on both stores, so
            // without a version bump the swap would go unnoticed. The rebuilt resolver must report a
            // strictly larger version for the same source so the open reader reopens against the new store.
            string sourceKey = Path.Combine(first, "clip.mov");
            long versionBefore = services.ResolverFacade.GetSourceVersion(sourceKey);

            config.StoreRootPath = second;

            long versionAfter = services.ResolverFacade.GetSourceVersion(sourceKey);
            Assert.That(versionAfter, Is.GreaterThan(versionBefore));
        }
        finally
        {
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Restore(config, prior);
            TryDelete(first);
            TryDelete(second);
        }
    }

    [AvaloniaTest]
    public void Store_facade_forwards_Changed_events_across_a_rebuild()
    {
        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        (string Root, long Cap) prior = Snapshot(config);
        string first = Path.Combine(Path.GetTempPath(), $"proxy-reinit-{Guid.NewGuid():N}");
        string second = Path.Combine(Path.GetTempPath(), $"proxy-reinit-{Guid.NewGuid():N}");
        config.StoreRootPath = first;

        ProxyMediaServices services = ProxyMediaServices.Initialize(GlobalConfiguration.Instance);
        int changedCount = 0;
        void Handler(object? sender, ProxyStoreChangedEventArgs e) => changedCount++;
        services.StoreFacade.Changed += Handler;
        try
        {
            config.StoreRootPath = second;

            // A subscriber bound to the facade before the rebuild must receive events from the new store.
            var fingerprint = new ProxyFingerprint(Path.Combine(second, "source.mov"), 1, DateTime.UtcNow);
            services.Store.Register(new ProxyEntry(
                fingerprint, ProxyPreset.Quarter, ProxyState.Generating, $"proxy/{Guid.NewGuid():N}.mp4", 0,
                new PixelSize(100, 100), default, DateTime.UtcNow, DateTime.UtcNow, null));

            Assert.That(changedCount, Is.GreaterThan(0));
        }
        finally
        {
            services.StoreFacade.Changed -= Handler;
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Restore(config, prior);
            TryDelete(first);
            TryDelete(second);
        }
    }

    [AvaloniaTest]
    public void Store_facade_raises_Reset_on_store_swap()
    {
        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        (string Root, long Cap) prior = Snapshot(config);
        string first = Path.Combine(Path.GetTempPath(), $"proxy-reinit-{Guid.NewGuid():N}");
        string second = Path.Combine(Path.GetTempPath(), $"proxy-reinit-{Guid.NewGuid():N}");
        config.StoreRootPath = first;

        ProxyMediaServices services = ProxyMediaServices.Initialize(GlobalConfiguration.Instance);
        int resetCount = 0;
        void Handler(object? sender, ProxyStoreChangedEventArgs e)
        {
            if (e.Kind == ProxyStoreChangeKind.Reset)
                resetCount++;
        }

        services.StoreFacade.Changed += Handler;
        try
        {
            // The new store loads its index in the constructor (no Registered events), so a swap must
            // raise a Reset for open Proxies tabs / badges to refresh to the new store's state.
            config.StoreRootPath = second;

            Assert.That(resetCount, Is.EqualTo(1));
        }
        finally
        {
            services.StoreFacade.Changed -= Handler;
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Restore(config, prior);
            TryDelete(first);
            TryDelete(second);
        }
    }

    [AvaloniaTest]
    public void StoreRootPath_caseOnlyChange_doesNotRebuild()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            Assert.Ignore("Case-insensitive path comparison only applies on Windows/macOS.");
        }

        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        (string Root, long Cap) prior = Snapshot(config);
        string lower = Path.Combine(Path.GetTempPath(), $"proxy-case-{Guid.NewGuid():N}");
        config.StoreRootPath = lower;

        ProxyMediaServices services = ProxyMediaServices.Initialize(GlobalConfiguration.Instance);
        try
        {
            ProxyStore storeBefore = services.Store;
            ProxyJobQueue queueBefore = services.Queue;

            // Same directory, different case: on Windows/macOS this is the same filesystem location, so
            // the store/queue must not be torn down (which would cancel in-flight jobs).
            config.StoreRootPath = lower.ToUpperInvariant();

            Assert.Multiple(() =>
            {
                Assert.That(services.Store, Is.SameAs(storeBefore));
                Assert.That(services.Queue, Is.SameAs(queueBefore));
            });
        }
        finally
        {
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Restore(config, prior);
            TryDelete(lower);
        }
    }

    [AvaloniaTest]
    public void Store_reset_fires_after_resolver_and_queue_facades_repoint()
    {
        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        (string Root, long Cap) prior = Snapshot(config);
        string first = Path.Combine(Path.GetTempPath(), $"proxy-reinit-{Guid.NewGuid():N}");
        string second = Path.Combine(Path.GetTempPath(), $"proxy-reinit-{Guid.NewGuid():N}");
        config.StoreRootPath = first;

        ProxyMediaServices services = ProxyMediaServices.Initialize(GlobalConfiguration.Instance);
        string sourceKey = Path.Combine(first, "clip.mov");
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

        services.StoreFacade.Changed += Handler;
        try
        {
            config.StoreRootPath = second;

            Assert.That(versionAtReset, Is.GreaterThan(versionBefore));
        }
        finally
        {
            services.StoreFacade.Changed -= Handler;
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Restore(config, prior);
            TryDelete(first);
            TryDelete(second);
        }
    }

    [AvaloniaTest]
    public void StoreRootPath_invalidPath_keepsPreviousStore_andDoesNotThrow()
    {
        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        (string Root, long Cap) prior = Snapshot(config);
        string good = Path.Combine(Path.GetTempPath(), $"proxy-reinit-{Guid.NewGuid():N}");
        // An existing file cannot be opened as a store directory: ProxyStore's Directory.CreateDirectory
        // throws, and the rebuild must swallow that and keep the live store rather than escaping the setter.
        string badFile = Path.Combine(Path.GetTempPath(), $"proxy-reinit-file-{Guid.NewGuid():N}");
        File.WriteAllBytes(badFile, [1]);
        config.StoreRootPath = good;

        ProxyMediaServices services = ProxyMediaServices.Initialize(GlobalConfiguration.Instance);
        try
        {
            ProxyStore storeBefore = services.Store;

            Assert.DoesNotThrow(() => config.StoreRootPath = badFile);

            Assert.Multiple(() =>
            {
                Assert.That(services.Store, Is.SameAs(storeBefore));
                Assert.That(services.Store.StoreRootPath, Is.EqualTo(Path.GetFullPath(good)));
                Assert.That(services.StoreFacade.StoreRootPath, Is.EqualTo(Path.GetFullPath(good)));
            });
        }
        finally
        {
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Restore(config, prior);
            TryDelete(good);
            try { File.Delete(badFile); } catch { /* best-effort test cleanup */ }
        }
    }

    [AvaloniaTest]
    public void Store_swap_completes_even_if_a_Reset_subscriber_throws()
    {
        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        (string Root, long Cap) prior = Snapshot(config);
        string first = Path.Combine(Path.GetTempPath(), $"proxy-reinit-{Guid.NewGuid():N}");
        string second = Path.Combine(Path.GetTempPath(), $"proxy-reinit-{Guid.NewGuid():N}");
        config.StoreRootPath = first;

        ProxyMediaServices services = ProxyMediaServices.Initialize(GlobalConfiguration.Instance);
        void Throwing(object? sender, ProxyStoreChangedEventArgs e) => throw new InvalidOperationException("boom");
        services.StoreFacade.Changed += Throwing;
        try
        {
            // A throwing Reset subscriber must not abort the swap: the other facades still repoint.
            config.StoreRootPath = second;

            Assert.Multiple(() =>
            {
                Assert.That(services.Store.StoreRootPath, Is.EqualTo(Path.GetFullPath(second)));
                Assert.That(services.ResolverFacade, Is.Not.Null);
                Assert.That(services.QueueFacade, Is.Not.Null);
            });
        }
        finally
        {
            services.StoreFacade.Changed -= Throwing;
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Restore(config, prior);
            TryDelete(first);
            TryDelete(second);
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }
}
