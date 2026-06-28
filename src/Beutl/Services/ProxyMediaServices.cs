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
    private static readonly ILogger s_logger = Log.CreateLogger<ProxyMediaServices>();
    private bool _disposed;

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
                $"Evicted {result.RemovedCount} proxy file(s), reclaimed {FormatBytes(result.ReclaimedBytes)}."));

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
        if (e.Kind == ProxyJobChangeKind.Succeeded)
            _ = Task.Run(() => SweepBestEffort(EvictionService));
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
