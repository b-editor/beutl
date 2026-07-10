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

            config.MaxTotalBytes = 16L * 1024 * 1024 * 1024;

            Assert.Multiple(() =>
            {
                Assert.That(services.CapInfoFacade, Is.SameAs(capFacade));
                Assert.That(capFacade.MaxTotalBytes, Is.EqualTo(16L * 1024 * 1024 * 1024));
                // The store root did not change, so the facade must keep serving that same path.
                Assert.That(services.StoreFacade.StoreRootPath, Is.EqualTo(Path.GetFullPath(root)));
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
