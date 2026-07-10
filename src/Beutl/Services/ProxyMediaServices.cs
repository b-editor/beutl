using Avalonia.Threading;

using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Logging;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal sealed class ProxyMediaServices : IAsyncDisposable
{
    // Host free-disk headroom kept on the store drive, independent of MaxTotalBytes.
    private const long DefaultMinFreeDiskBytes = 2L * 1024 * 1024 * 1024;

    private static readonly ILogger s_logger = Log.CreateLogger<ProxyMediaServices>();
    private static readonly IReadOnlySet<string> s_noOpenProjectSources = new HashSet<string>();

    // Set once shutdown starts. Only then must source collection skip the UI-thread marshal: app exit
    // awaits the queue drain while the drain-path sweep would await the UI thread -> deadlock. During
    // normal generation the UI thread is not blocked on the drain, so marshaling is safe (and keeps a
    // consistent snapshot), so this stays false there.
    private static volatile bool s_disposing;

    private bool _disposed;
    private int _diskPressureSweepActive;
    private IDisposable? _configSubscription;

    private ProxyMediaServices(
        ProxyStore store,
        ProxyResolver resolver,
        ProxyJobQueue queue,
        ProxyEvictionService evictionService)
    {
        Store = store;
        Resolver = resolver;
        Queue = queue;
        EvictionService = evictionService;
        StoreFacade = new StableProxyStoreFacade(store);
        ResolverFacade = new StableProxyResolverFacade(resolver);
        QueueFacade = new StableProxyQueueFacade(queue);
        CapInfoFacade = new StableProxyCapInfoFacade(evictionService);
        Queue.JobChanged += OnJobChanged;
        // A proxy extension registers its generator factory after this queue is built and unregisters it
        // on unload; drop the queue's cached generator on either so it re-resolves and never keeps
        // rooting or invoking an unloaded extension's generator.
        ProxyGeneratorRegistry.Changed += OnGeneratorRegistryChanged;
    }

    private void OnGeneratorRegistryChanged(object? sender, EventArgs e) => Queue.InvalidateGenerator();

    public static ProxyMediaServices? Current { get; private set; }

    public ProxyStore Store { get; private set; }

    public ProxyResolver Resolver { get; private set; }

    public ProxyJobQueue Queue { get; private set; }

    public ProxyEvictionService EvictionService { get; private set; }

    // Swap-stable views handed to editor ViewModels; the backing service is rebuilt on a store-root /
    // cap change while the facade instance the ViewModel cached (and subscribed to) stays put.
    public StableProxyStoreFacade StoreFacade { get; }

    public StableProxyResolverFacade ResolverFacade { get; }

    public StableProxyQueueFacade QueueFacade { get; }

    public StableProxyCapInfoFacade CapInfoFacade { get; }

    public static ProxyMediaServices Initialize(GlobalConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (Current is { } existing)
            return existing;

        ProxyStoreConfig config = configuration.ProxyStoreConfig;
        var (store, resolver, queue, eviction) = BuildServices(config.StoreRootPath, config.MaxTotalBytes);
        var services = new ProxyMediaServices(store, resolver, queue, eviction);
        s_disposing = false;
        Current = services;
        DecoderRegistry.ProxyResolver = resolver;

        // The store path and eviction cap are captured at construction, so observe both: a runtime
        // change rebuilds the live store/resolver/queue/eviction against the new values instead of
        // leaving Generate/Delete/Resolve on the startup directory or the eviction sweep on the old cap.
        // The setters fire on the UI thread and ReinitializeStore swaps synchronously, so consumers
        // re-fetching Current?.Store never see a torn mix. Skip(1) drops each observable's replay of the
        // current value so construction does not immediately rebuild.
        services._configSubscription = new CompositeDisposable(
            config.GetObservable(ProxyStoreConfig.StoreRootPathProperty).Skip(1)
                .Subscribe(_ => services.ReinitializeStore(config.StoreRootPath, config.MaxTotalBytes)),
            config.GetObservable(ProxyStoreConfig.MaxTotalBytesProperty).Skip(1)
                .Subscribe(_ => services.ReinitializeStore(config.StoreRootPath, config.MaxTotalBytes)));

        _ = Task.Run(() => ReconcileAndSweepAsync(store, eviction));

        return services;
    }

    private static (ProxyStore Store, ProxyResolver Resolver, ProxyJobQueue Queue, ProxyEvictionService Eviction)
        BuildServices(string storeRootPath, long maxTotalBytes)
    {
        var store = new ProxyStore(storeRootPath);
        var resolver = new ProxyResolver(store);
        ProxyJobQueue queue = null!;
        var eviction = new ProxyEvictionService(
            store,
            resolver,
            maxTotalBytes,
            result => NotificationService.ShowInformation(
                "Proxy media",
                $"Evicted {result.RemovedCount} proxy file(s), reclaimed {FormatBytes(result.ReclaimedBytes)}."),
            minFreeDiskBytes: DefaultMinFreeDiskBytes,
            openProjectSourceProvider: CollectOpenProjectSources,
            activeGenerationProvider: () => CollectActiveGenerations(queue));
        queue = new ProxyJobQueue(ResolveGeneratorFactory(store, eviction), store);
        return (store, resolver, queue, eviction);
    }

    // The generator is resolved lazily from ProxyGeneratorRegistry on the first job dispatch: the
    // FFmpeg proxy extension registers its factory during the startup extension-load task, which runs
    // AFTER RegisterServices builds this queue, so the registry is still empty here. Returning null
    // while empty lets the queue re-probe and pick up a generator that registers later (a plugin, or
    // the built-in FFmpeg factory once its extension loads). The disk-headroom wrapper runs in the
    // dispatch path (right before the encoder writes) so a low-disk store does not fail the first job
    // eviction could have made room for; the availability-aware variant is a distinct type because the
    // queue type-tests for IProxyGeneratorAvailability — wrapping without it would fabricate a signal.
    private static Func<IProxyGenerator?> ResolveGeneratorFactory(ProxyStore store, ProxyEvictionService eviction)
    {
        return () =>
        {
            IProxyGeneratorFactory? factory = ProxyGeneratorRegistry.Enumerate().FirstOrDefault();
            if (factory is null)
                return null;

            IProxyGenerator generator = factory.Create(store);
            return generator is IProxyGeneratorAvailability
                ? new AvailabilityAwareDiskHeadroomProxyGenerator(generator, eviction)
                : new DiskHeadroomProxyGenerator(generator, eviction);
        };
    }

    private void ReinitializeStore(string storeRootPath, long maxTotalBytes)
    {
        if (_disposed)
            return;

        // Swap on this (the config setter's) thread so consumers re-fetching Current?.Store never
        // observe a torn mix. The old queue is drained off-thread: its drain path marshals
        // project-graph reads through the UI thread, so awaiting it here would deadlock like shutdown.
        var (newStore, newResolver, newQueue, newEviction) = BuildServices(storeRootPath, maxTotalBytes);
        ProxyJobQueue oldQueue = Queue;
        ProxyResolver oldResolver = Resolver;

        oldQueue.JobChanged -= OnJobChanged;
        Store = newStore;
        Resolver = newResolver;
        Queue = newQueue;
        EvictionService = newEviction;
        newQueue.JobChanged += OnJobChanged;
        // Repoint the facades (and their forwarded event subscriptions) at the new backing services so
        // an open editor's cached IProxyStore/Queue keeps hitting the current store, not the disposed one.
        StoreFacade.Swap(newStore);
        ResolverFacade.Swap(newResolver);
        QueueFacade.Swap(newQueue);
        CapInfoFacade.Swap(newEviction);
        if (ReferenceEquals(DecoderRegistry.ProxyResolver, oldResolver))
        {
            DecoderRegistry.ProxyResolver = newResolver;
        }

        _ = Task.Run(() => ReconcileAndSweepAsync(newStore, newEviction));
        _ = Task.Run(async () =>
        {
            try
            {
                await oldQueue.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "Disposing the previous proxy queue after a store-root change failed.");
            }
        });
    }


    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        // Signal the drain-path sweep to stop marshaling source collection to the UI thread before we
        // await the queue drain below, so shutdown cannot deadlock on the UI thread.
        s_disposing = true;
        // Unsubscribe before disposing the queue so a late registry change cannot race Queue disposal.
        ProxyGeneratorRegistry.Changed -= OnGeneratorRegistryChanged;
        Queue.JobChanged -= OnJobChanged;
        _configSubscription?.Dispose();
        if (ReferenceEquals(DecoderRegistry.ProxyResolver, Resolver))
            DecoderRegistry.ProxyResolver = null;

        if (ReferenceEquals(Current, this))
            Current = null;

        await Queue.DisposeAsync().ConfigureAwait(false);
    }

    private void OnJobChanged(object? sender, ProxyJobChangedEventArgs e)
    {
        switch (e.Kind)
        {
            case ProxyJobChangeKind.Succeeded:
                _ = Task.Run(() => SweepBestEffort(EvictionService));
                break;

            // Free space before a queued job runs, and again if a job fails so the
            // next one has a chance. The sweep self-gates on real cap/disk pressure.
            case ProxyJobChangeKind.Enqueued:
            case ProxyJobChangeKind.Failed:
                TrySweepForDiskPressure();
                break;
        }
    }

    private void TrySweepForDiskPressure()
    {
        // Each sweep is a full project-graph traversal plus a full store enumeration, and a
        // burst (one job per clip when opening a project) fires this per Enqueued/Failed;
        // collapse concurrent triggers into a single in-flight sweep.
        if (Interlocked.CompareExchange(ref _diskPressureSweepActive, 1, 0) != 0)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                SweepForDiskPressureBestEffort(EvictionService);
            }
            finally
            {
                Volatile.Write(ref _diskPressureSweepActive, 0);
            }
        });
    }

    private static async Task ReconcileAndSweepAsync(ProxyStore store, ProxyEvictionService eviction)
    {
        try
        {
            await store.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            eviction.Sweep();
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Proxy store startup reconciliation failed.");
        }
    }

    private static void SweepBestEffort(ProxyEvictionService eviction)
    {
        try
        {
            eviction.Sweep();
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Proxy store eviction failed.");
        }
    }

    private static void SweepForDiskPressureBestEffort(ProxyEvictionService eviction)
    {
        try
        {
            // Routine pre-job headroom sweep: evict silently. Cap-overage notifications
            // are surfaced by the post-Succeeded/startup Sweep instead.
            eviction.SweepForDiskPressure(notify: false);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Proxy store disk-pressure eviction failed.");
        }
    }

    private static IReadOnlySet<(ProxyFingerprint Source, ProxyPreset Preset)> CollectActiveGenerations(ProxyJobQueue queue)
    {
        var active = new HashSet<(ProxyFingerprint, ProxyPreset)>();
        foreach (ProxyJob job in queue.Pending())
            active.Add((job.Source, job.Preset));

        return active;
    }

    private static IReadOnlySet<string> CollectOpenProjectSources()
    {
        // Eviction sweeps run on background threads, but this walks project / element / node-graph
        // state that the UI thread mutates. Read it on the UI thread so the collected set is a
        // consistent snapshot rather than a torn read that could fall back to global LRU — except in
        // the drain-path sweep, where blocking on the UI thread would deadlock shutdown (see the flag).
        if (!s_disposing && !Dispatcher.UIThread.CheckAccess())
        {
            try
            {
                return Dispatcher.UIThread.Invoke(CollectOpenProjectSources);
            }
            catch
            {
                return s_noOpenProjectSources;
            }
        }

        try
        {
            if (BeutlApplication.Current.Project is not { } project)
                return s_noOpenProjectSources;

            return ProxySourceEnumerator.EnumerateFileSources(project);
        }
        catch
        {
            // Best-effort: the project graph may be mutated on the UI thread while this
            // runs on a sweep thread. On any failure fall back to global LRU.
            return s_noOpenProjectSources;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    // Runs a disk-pressure sweep synchronously in the queue's dispatch path, right before the inner
    // generator writes, and forwards the inner generator's availability signal unchanged.
    private class DiskHeadroomProxyGenerator(IProxyGenerator inner, ProxyEvictionService eviction)
        : IProxyGenerator
    {
        public ValueTask GenerateAsync(ProxyJob job)
        {
            SweepForDiskPressureBestEffort(eviction);
            return inner.GenerateAsync(job);
        }
    }

    private sealed class AvailabilityAwareDiskHeadroomProxyGenerator(
        IProxyGenerator inner,
        ProxyEvictionService eviction)
        : DiskHeadroomProxyGenerator(inner, eviction), IProxyGeneratorAvailability
    {
        private readonly IProxyGeneratorAvailability _availability = (IProxyGeneratorAvailability)inner;

        public bool IsAvailable => _availability.IsAvailable;

        public event EventHandler? AvailabilityChanged
        {
            add => _availability.AvailabilityChanged += value;
            remove => _availability.AvailabilityChanged -= value;
        }
    }
}
