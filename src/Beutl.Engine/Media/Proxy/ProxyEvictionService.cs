namespace Beutl.Media.Proxy;

public sealed class ProxyEvictionService
{
    private static readonly IReadOnlySet<string> s_noProtectedSources = new HashSet<string>();

    private readonly IProxyStore _store;
    private readonly ProxyResolver? _resolver;
    private readonly long _maxTotalBytes;
    private readonly long _minFreeDiskBytes;
    private readonly Action<ProxyEvictionResult>? _notify;
    private readonly Func<IReadOnlySet<string>>? _openProjectSourceProvider;
    private readonly Func<string, long?>? _availableFreeSpaceProvider;

    public ProxyEvictionService(
        IProxyStore store,
        ProxyResolver? resolver,
        long maxTotalBytes,
        Action<ProxyEvictionResult>? notify = null,
        long minFreeDiskBytes = 0,
        Func<IReadOnlySet<string>>? openProjectSourceProvider = null,
        Func<string, long?>? availableFreeSpaceProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfNegative(minFreeDiskBytes);

        _store = store;
        _resolver = resolver;
        _maxTotalBytes = maxTotalBytes;
        _minFreeDiskBytes = minFreeDiskBytes;
        _notify = notify;
        _openProjectSourceProvider = openProjectSourceProvider;
        _availableFreeSpaceProvider = availableFreeSpaceProvider;
    }

    /// <summary>
    /// Evicts least-recently-used proxies until the store is back under the app cap
    /// (<c>MaxTotalBytes</c>). Proxies referenced by the currently-open project are
    /// evicted last, only when nothing else can satisfy the cap.
    /// </summary>
    public ProxyEvictionResult Sweep()
    {
        return EvictAndNotify(CapOverage(), diskShortfall: 0);
    }

    /// <summary>
    /// Evicts proxies in reaction to disk pressure: it enforces the app cap and, in
    /// addition, restores host free-disk headroom (independent of the cap) so a
    /// queued generation job has room to write. Intended to run before a job starts
    /// and after a job fails. If eviction cannot plausibly restore the headroom it
    /// leaves proxies intact rather than deleting unrelated proxies for nothing.
    /// </summary>
    /// <param name="additionalBytesNeeded">
    /// Extra free space required on top of the configured headroom (e.g. an estimate
    /// of the job's output size). Must be non-negative.
    /// </param>
    public ProxyEvictionResult SweepForDiskPressure(long additionalBytesNeeded = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(additionalBytesNeeded);

        return EvictAndNotify(CapOverage(), DiskShortfall(additionalBytesNeeded));
    }

    private long CapOverage()
    {
        return Math.Max(0, _store.GetTotalBytes() - _maxTotalBytes);
    }

    private long DiskShortfall(long additionalBytesNeeded)
    {
        if (GetAvailableFreeSpace(_store.StoreRootPath) is not { } available)
            return 0;

        long target = _minFreeDiskBytes + additionalBytesNeeded;
        return Math.Max(0, target - available);
    }

    private ProxyEvictionResult EvictAndNotify(long capOverage, long diskShortfall)
    {
        ProxyEvictionResult result = Evict(capOverage, diskShortfall);
        if (result.RemovedCount > 0)
            _notify?.Invoke(result);

        return result;
    }

    private ProxyEvictionResult Evict(long capOverage, long diskShortfall)
    {
        if (capOverage <= 0 && diskShortfall <= 0)
            return default;

        IReadOnlySet<string> protectedSources = ResolveProtectedSources();

        List<Candidate> candidates = [];
        foreach (ProxyEntry entry in _store.Enumerate()
                     .Where(static e => e.State is ProxyState.Ready or ProxyState.Stale or ProxyState.Failed))
        {
            if (!ProxyPathUtilities.TryResolveRelativePath(_store.StoreRootPath, entry.ProxyFileRelative, out string absolutePath))
                continue;

            if (_resolver?.IsPinned(absolutePath) == true)
                continue;

            long bytes = File.Exists(absolutePath)
                ? new FileInfo(absolutePath).Length
                : entry.ProxyFileSizeBytes;

            bool isProtected = protectedSources.Contains(entry.Source.AbsolutePath);
            candidates.Add(new Candidate(entry, bytes, isProtected));
        }

        // Non-protected LRU candidates first; open-project proxies only as a last resort.
        candidates.Sort(static (a, b) =>
        {
            int byProtection = a.IsProtected.CompareTo(b.IsProtected);
            return byProtection != 0
                ? byProtection
                : a.Entry.LastUsedUtc.CompareTo(b.Entry.LastUsedUtc);
        });

        long totalReclaimable = candidates.Sum(static c => c.Bytes);

        // Restoring disk headroom is only worthwhile if eviction can actually reach it;
        // otherwise pursue the cap goal alone rather than delete proxies for nothing.
        long diskTarget = totalReclaimable >= diskShortfall ? diskShortfall : 0;
        long bytesToFree = Math.Max(capOverage, diskTarget);
        if (bytesToFree <= 0)
            return default;

        int removed = 0;
        long reclaimed = 0;
        foreach (Candidate candidate in candidates)
        {
            if (reclaimed >= bytesToFree)
                break;

            if (_store.Delete(candidate.Entry.Source, candidate.Entry.Preset))
            {
                removed++;
                reclaimed += candidate.Bytes;
            }
        }

        return new ProxyEvictionResult(removed, reclaimed);
    }

    private IReadOnlySet<string> ResolveProtectedSources()
    {
        if (_openProjectSourceProvider == null)
            return s_noProtectedSources;

        IReadOnlySet<string>? provided;
        try
        {
            provided = _openProjectSourceProvider();
        }
        catch
        {
            return s_noProtectedSources;
        }

        if (provided is not { Count: > 0 })
            return s_noProtectedSources;

        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in provided)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            try
            {
                normalized.Add(ProxyFingerprint.NormalizeAbsolutePath(path));
            }
            catch
            {
                // Ignore unparseable paths; they simply do not protect any entry.
            }
        }

        return normalized;
    }

    private long? GetAvailableFreeSpace(string storeRootPath)
    {
        if (_availableFreeSpaceProvider != null)
        {
            try
            {
                return _availableFreeSpaceProvider(storeRootPath);
            }
            catch
            {
                return null;
            }
        }

        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(storeRootPath));
            if (string.IsNullOrEmpty(root))
                return null;

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    private readonly record struct Candidate(ProxyEntry Entry, long Bytes, bool IsProtected);
}

public readonly record struct ProxyEvictionResult(int RemovedCount, long ReclaimedBytes);
