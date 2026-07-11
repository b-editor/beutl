namespace Beutl.Media.Proxy;

public sealed class ProxyEvictionService : IProxyStoreCapInfo
{
    private static readonly IReadOnlySet<string> s_noProtectedSources = new HashSet<string>();

    private readonly IProxyStore _store;
    private readonly IProxyResolver? _resolver;
    private long _maxTotalBytes;
    private readonly long _minFreeDiskBytes;
    private readonly Action<ProxyEvictionResult>? _notify;
    private readonly Func<IReadOnlySet<string>>? _openProjectSourceProvider;
    private readonly Func<string, long?>? _availableFreeSpaceProvider;
    private readonly Func<ProxyFingerprint, ProxyPreset, bool>? _isGenerationActive;

    // Mutable so a runtime cache-cap change updates the threshold in place; rebuilding the service (and
    // its queue) just to move this number would cancel unrelated in-flight generation. Read on sweep
    // threads, written from the config setter — Volatile keeps the write visible without a torn read.
    public long MaxTotalBytes
    {
        get => Volatile.Read(ref _maxTotalBytes);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Volatile.Write(ref _maxTotalBytes, value);
        }
    }

    /// <param name="openProjectSourceProvider">
    /// Returns the absolute paths of media files referenced by the currently-open project so
    /// those proxies are evicted last (only when nothing else satisfies the cap). The set is
    /// compared by <see cref="StringComparer.Ordinal"/> after path normalization. This callback
    /// is invoked once per <see cref="Sweep"/> and <see cref="SweepForDiskPressure"/> call; it
    /// must be O(1) or memoized by the consumer (e.g. a cached snapshot invalidated on project
    /// open/close), not a fresh per-call enumeration of the project tree.
    /// </param>
    /// <param name="isGenerationActive">
    /// Tests whether a proxy generation job for the given (source, preset) is currently in flight so
    /// the eviction never deletes a proxy whose replacement is still being written. A predicate rather
    /// than a set snapshot: the sweep queries it once per candidate immediately before deleting (to see
    /// regenerations queued mid-sweep), so it must be a cheap membership test, not a per-call rebuild of
    /// the whole active set.
    /// </param>
    public ProxyEvictionService(
        IProxyStore store,
        IProxyResolver? resolver,
        long maxTotalBytes,
        Action<ProxyEvictionResult>? notify = null,
        long minFreeDiskBytes = 0,
        Func<IReadOnlySet<string>>? openProjectSourceProvider = null,
        Func<string, long?>? availableFreeSpaceProvider = null,
        Func<ProxyFingerprint, ProxyPreset, bool>? isGenerationActive = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTotalBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(minFreeDiskBytes);

        _store = store;
        _resolver = resolver;
        _maxTotalBytes = maxTotalBytes;
        _minFreeDiskBytes = minFreeDiskBytes;
        _notify = notify;
        _openProjectSourceProvider = openProjectSourceProvider;
        _availableFreeSpaceProvider = availableFreeSpaceProvider;
        _isGenerationActive = isGenerationActive;
    }

    /// <summary>
    /// Evicts least-recently-used proxies until the store is back under the app cap
    /// (<c>MaxTotalBytes</c>). Proxies referenced by the currently-open project are
    /// evicted last, only when nothing else can satisfy the cap.
    /// </summary>
    public ProxyEvictionResult Sweep()
    {
        return EvictAndNotify(CapOverage(), diskShortfall: 0, notify: true);
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
    /// <param name="notify">
    /// When <see langword="false"/>, evictions performed by this sweep are not reported
    /// through the notification callback. The routine pre-job disk-pressure path passes
    /// <see langword="false"/> so it does not spam the user; the cap-overage <see cref="Sweep"/>
    /// path notifies instead.
    /// </param>
    public ProxyEvictionResult SweepForDiskPressure(long additionalBytesNeeded = 0, bool notify = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(additionalBytesNeeded);

        return EvictAndNotify(CapOverage(), DiskShortfall(additionalBytesNeeded), notify);
    }

    private long CapOverage()
    {
        // Read through the property so a runtime cap change (Volatile.Write) is observed on sweep threads.
        return Math.Max(0, _store.GetTotalBytes() - MaxTotalBytes);
    }

    private long DiskShortfall(long additionalBytesNeeded)
    {
        if (GetAvailableFreeSpace(_store.StoreRootPath) is not { } available)
            return 0;

        long target = _minFreeDiskBytes + additionalBytesNeeded;
        return Math.Max(0, target - available);
    }

    private ProxyEvictionResult EvictAndNotify(long capOverage, long diskShortfall, bool notify)
    {
        ProxyEvictionResult result = Evict(capOverage, diskShortfall);
        if (notify && result.RemovedCount > 0)
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

            // A proxy queued for regeneration must not be deleted before its replacement is
            // registered; if that generation is later canceled or fails the user would lose the
            // still-usable proxy for nothing.
            if (_isGenerationActive?.Invoke(entry.Source, entry.Preset) == true)
                continue;

            // Cap accounting must use the recorded size, because capOverage / GetTotalBytes sum
            // ProxyFileSizeBytes — crediting a larger on-disk length here could stop the sweep while
            // the store is still over MaxTotalBytes. Disk accounting uses the actual on-disk length
            // (0 when the file is already gone, so a missing file frees no disk headroom).
            long onDiskBytes = TryGetFileLength(absolutePath) ?? 0;

            bool isProtected = protectedSources.Contains(entry.Source.AbsolutePath);
            candidates.Add(new Candidate(entry, entry.ProxyFileSizeBytes, onDiskBytes, isProtected));
        }

        // Non-protected LRU candidates first; open-project proxies only as a last resort.
        candidates.Sort(static (a, b) =>
        {
            int byProtection = a.IsProtected.CompareTo(b.IsProtected);
            return byProtection != 0
                ? byProtection
                : a.Entry.LastUsedUtc.CompareTo(b.Entry.LastUsedUtc);
        });

        // Disk headroom can only be freed by actually-present files; restoring it is worthwhile only
        // if eviction can reach it, otherwise pursue the cap goal alone rather than delete for nothing.
        long totalOnDiskReclaimable = candidates.Sum(static c => c.OnDiskBytes);
        long diskTarget = totalOnDiskReclaimable >= diskShortfall ? diskShortfall : 0;
        long capTarget = Math.Max(capOverage, 0);
        if (capTarget <= 0 && diskTarget <= 0)
            return default;

        int removed = 0;
        long reclaimedCap = 0;
        long reclaimedDisk = 0;
        foreach (Candidate candidate in candidates)
        {
            // Stop once both goals are met. A missing proxy contributes its recorded size to the cap
            // goal but 0 to the disk goal, so it cannot end a disk-pressure sweep early.
            if (reclaimedCap >= capTarget && reclaimedDisk >= diskTarget)
                break;

            // Re-check the pin immediately before deleting: a ProxyMediaReader may have pinned this
            // proxy after candidate collection, and the collection-time snapshot would miss it.
            if (_resolver != null
                && ProxyPathUtilities.TryResolveRelativePath(_store.StoreRootPath, candidate.Entry.ProxyFileRelative, out string pinnedPath)
                && _resolver.IsPinned(pinnedPath))
                continue;

            // Test the active generation immediately before each delete, mirroring the pin re-check
            // above: a regenerate for this key may have been queued while the sweep deleted earlier
            // candidates, so a check taken once before the loop would miss it and this delete would drop
            // a proxy whose replacement is still in flight (losing the usable fallback if that generation
            // later fails). The predicate is a cheap per-key membership test, not a per-candidate rebuild
            // of the whole active set.
            // Delete keys on (Source, Preset), so a regenerate that ran since collection would make this
            // remove the current (possibly freshly Ready) entry rather than the ranked one. Skip unless the
            // current entry is still the ranked proxy and no generation for the key is active. Compare only
            // identity fields (proxy file + generation time), not the whole record: ProxyStore.Touch
            // replaces the entry to bump LastUsedUtc, so a full-record check would read a normal playback
            // touch between collection and here as a regenerate and starve eviction while over cap.
            ProxyEntry? current = _store.TryGet(candidate.Entry.Source, candidate.Entry.Preset);
            if (current is null
                || current.ProxyFileRelative != candidate.Entry.ProxyFileRelative
                || current.GeneratedAtUtc != candidate.Entry.GeneratedAtUtc
                || _isGenerationActive?.Invoke(candidate.Entry.Source, candidate.Entry.Preset) == true)
                continue;

            if (_store.Delete(candidate.Entry.Source, candidate.Entry.Preset))
            {
                removed++;
                reclaimedCap += candidate.Bytes;

                // Store.Delete removes the index entry but only best-effort-deletes the file (a sharing
                // violation / permission error leaves an orphan). Credit disk reclamation only once the
                // file is confirmed gone, or a disk-pressure sweep stops early believing it freed space
                // the orphan still occupies — and the orphan is now untracked, so no later sweep reclaims it.
                if (candidate.OnDiskBytes > 0
                    && ProxyPathUtilities.TryResolveRelativePath(_store.StoreRootPath, candidate.Entry.ProxyFileRelative, out string deletedPath)
                    && !File.Exists(deletedPath))
                {
                    reclaimedDisk += candidate.OnDiskBytes;
                }
            }
        }

        return new ProxyEvictionResult(removed, reclaimedDisk);
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
                normalized.Add(ResolveComparablePath(path));
            }
            catch
            {
                // Ignore unparseable paths; they simply do not protect any entry.
            }
        }

        return normalized;
    }

    private static long? TryGetFileLength(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveComparablePath(string path)
        => ProxyFingerprint.ResolveComparableKey(path);

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

    private readonly record struct Candidate(ProxyEntry Entry, long Bytes, long OnDiskBytes, bool IsProtected);
}

public readonly record struct ProxyEvictionResult(int RemovedCount, long ReclaimedBytes);
