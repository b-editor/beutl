using Beutl.Configuration;
using Beutl.Logging;
#if FFMPEG_BUILD_IN
using Beutl.Extensions.FFmpeg.Proxy;
#endif
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal sealed class ProxyMediaServices : IAsyncDisposable
{
    // Host free-disk headroom kept on the store drive, independent of MaxTotalBytes.
    private const long DefaultMinFreeDiskBytes = 2L * 1024 * 1024 * 1024;

    private static readonly ILogger s_logger = Log.CreateLogger<ProxyMediaServices>();
    private static readonly IReadOnlySet<string> s_noOpenProjectSources = new HashSet<string>();
    private bool _disposed;
    private int _diskPressureSweepActive;

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
        Queue.JobChanged += OnJobChanged;
    }

    public static ProxyMediaServices? Current { get; private set; }

    public ProxyStore Store { get; }

    public ProxyResolver Resolver { get; }

    public ProxyJobQueue Queue { get; }

    public ProxyEvictionService EvictionService { get; }

    public static ProxyMediaServices Initialize(GlobalConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (Current is { } existing)
            return existing;

        ProxyStoreConfig config = configuration.ProxyStoreConfig;
        var store = new ProxyStore(config.StoreRootPath);
        var resolver = new ProxyResolver(store);
        var queue = new ProxyJobQueue(CreateGenerator(store), store);
        var eviction = new ProxyEvictionService(
            store,
            resolver,
            config.MaxTotalBytes,
            result => NotificationService.ShowInformation(
                "Proxy media",
                $"Evicted {result.RemovedCount} proxy file(s), reclaimed {FormatBytes(result.ReclaimedBytes)}."),
            minFreeDiskBytes: DefaultMinFreeDiskBytes,
            openProjectSourceProvider: CollectOpenProjectSources);

        var services = new ProxyMediaServices(store, resolver, queue, eviction);
        Current = services;
        DecoderRegistry.ProxyResolver = resolver;

        _ = Task.Run(() => ReconcileAndSweepAsync(store, eviction));

        return services;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        Queue.JobChanged -= OnJobChanged;
        if (ReferenceEquals(DecoderRegistry.ProxyResolver, Resolver))
            DecoderRegistry.ProxyResolver = null;

        if (ReferenceEquals(Current, this))
            Current = null;

        await Queue.DisposeAsync().ConfigureAwait(false);
    }

    private static IProxyGenerator CreateGenerator(IProxyStore store)
    {
#if FFMPEG_BUILD_IN
        return new FFmpegProxyGenerator(store);
#else
        return new UnavailableProxyGenerator();
#endif
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

    private static IReadOnlySet<string> CollectOpenProjectSources()
    {
        try
        {
            if (BeutlApplication.Current.Project is not { } project)
                return s_noOpenProjectSources;

            // Every IFileSource the project references, in- or out-of-project. Affinity must
            // cover media stored under the project folder too; ExternalResourceCollector would
            // filter those out, leaving in-project workflows on plain LRU.
            return ProxyEvictionService.CollectProjectFileSources(project);
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

    private sealed class UnavailableProxyGenerator : IProxyGenerator
    {
        public ValueTask GenerateAsync(ProxyJob job)
        {
            throw new ProxyGeneratorUnavailableException("FFmpeg proxy generation is not available in this build.");
        }
    }
}
