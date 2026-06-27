namespace Beutl.Media.Proxy;

public sealed class ProxyEvictionService(
    IProxyStore store,
    ProxyResolver? resolver,
    long maxTotalBytes,
    Action<ProxyEvictionResult>? notify = null)
{
    public ProxyEvictionResult Sweep()
    {
        long total = store.GetTotalBytes();
        if (total <= maxTotalBytes)
            return new ProxyEvictionResult(0, 0);

        int removed = 0;
        long reclaimed = 0;

        foreach (ProxyEntry entry in store.Enumerate()
                     .Where(static e => e.State is ProxyState.Ready or ProxyState.Stale or ProxyState.Failed)
                     .OrderBy(static e => e.LastUsedUtc))
        {
            if (total <= maxTotalBytes)
                break;

            string absolutePath = Path.GetFullPath(Path.Combine(
                store.StoreRootPath,
                entry.ProxyFileRelative.Replace('/', Path.DirectorySeparatorChar)));

            if (resolver?.IsPinned(absolutePath) == true)
                continue;

            long bytes = File.Exists(absolutePath)
                ? new FileInfo(absolutePath).Length
                : entry.ProxyFileSizeBytes;

            if (store.Delete(entry.Source, entry.Preset))
            {
                removed++;
                reclaimed += bytes;
                total -= bytes;
            }
        }

        var result = new ProxyEvictionResult(removed, reclaimed);
        if (removed > 0)
            notify?.Invoke(result);

        return result;
    }
}

public readonly record struct ProxyEvictionResult(int RemovedCount, long ReclaimedBytes);
