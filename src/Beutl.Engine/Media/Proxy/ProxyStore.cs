using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Logging;

using Microsoft.Extensions.Logging;

namespace Beutl.Media.Proxy;

public sealed class ProxyStore : IProxyStore
{
    // Bounds the cross-process index.lock spin (10 ms/attempt) while _lock is held, so a peer
    // instance holding the lock stalls hot-path readers (TryGet/Touch/Enumerate) for at most
    // ~0.5 s before DegradePersistence engages and replays on the next uncontended flush.
    private const int DefaultLockAcquireMaxAttempts = 50;

    private static readonly TimeSpan s_generatedTempCleanupMinAge = TimeSpan.FromHours(1);

    private static readonly ILogger s_logger = Log.CreateLogger<ProxyStore>();

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Lock _lock = new();
    private readonly Dictionary<(ProxyFingerprint Source, ProxyPreset Preset), ProxyEntry> _entries = [];
    private readonly HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> _touchDirtyKeys = [];

    // Changes whose durable write was skipped under lock contention; re-applied on the
    // next successful flush so a briefly-contended lock never permanently loses them.
    private readonly HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> _pendingPersistKeys = [];
    private readonly HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> _pendingRemoveKeys = [];
    private readonly string _indexPath;
    private readonly string _indexLockPath;
    private readonly int _lockAcquireMaxAttempts;
    private int _touchFlushScheduled;
    private int _touchFlushFaulted;
    private bool _touchDirty;
    private bool _persistenceDegraded;

    public ProxyStore(string storeRootPath)
        : this(storeRootPath, DefaultLockAcquireMaxAttempts)
    {
    }

    internal ProxyStore(string storeRootPath, int lockAcquireMaxAttempts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRootPath);
        ArgumentOutOfRangeException.ThrowIfNegative(lockAcquireMaxAttempts);
        StoreRootPath = Path.GetFullPath(storeRootPath);
        _indexPath = Path.Combine(StoreRootPath, "index.json");
        _indexLockPath = Path.Combine(StoreRootPath, "index.lock");
        _lockAcquireMaxAttempts = lockAcquireMaxAttempts;
        Directory.CreateDirectory(StoreRootPath);
        LoadIndex();
    }

    public string StoreRootPath { get; }

    internal bool IsPersistenceDegraded
    {
        get
        {
            lock (_lock)
            {
                return _persistenceDegraded;
            }
        }
    }

    public event EventHandler<ProxyStoreChangedEventArgs>? Changed;

    public ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset)
    {
        lock (_lock)
        {
            return _entries.GetValueOrDefault((source, preset));
        }
    }

    public IReadOnlyList<ProxyEntry> Enumerate()
    {
        lock (_lock)
        {
            return [.. _entries.Values];
        }
    }

    public void Register(ProxyEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!TryValidateEntry(entry))
            throw new ArgumentException("Proxy entry is invalid.", nameof(entry));

        lock (_lock)
        {
            var key = GetKey(entry);
            _entries[key] = entry;
            FlushCore(changedKeys: new HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> { key });
        }

        OnChanged(entry.Source, entry.Preset, ProxyStoreChangeKind.Registered);
    }

    public bool TryTransition(
        ProxyFingerprint source,
        ProxyPreset preset,
        ProxyState newState,
        string? failureReason = null)
    {
        ProxyEntry updated;
        lock (_lock)
        {
            if (!_entries.TryGetValue((source, preset), out ProxyEntry? entry))
                return false;

            if (!ProxyStateTransitions.IsLegal(entry.State, newState))
                return false;

            updated = entry with
            {
                State = newState,
                GeneratedAtUtc = newState == ProxyState.Ready ? DateTime.UtcNow : entry.GeneratedAtUtc,
                LastUsedUtc = DateTime.UtcNow,
                FailureReason = newState == ProxyState.Failed ? failureReason : null,
            };
            var key = (source, preset);
            _entries[key] = updated;
            FlushCore(changedKeys: new HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> { key });
        }

        OnChanged(source, preset, ProxyStoreChangeKind.StateChanged);
        return true;
    }

    public bool Delete(ProxyFingerprint source, ProxyPreset preset)
    {
        ProxyEntry? removed = null;
        lock (_lock)
        {
            if (!_entries.TryGetValue((source, preset), out removed))
                return false;

            string absolutePath = GetAbsolutePath(removed);
            if (!TryDeleteProxyFile(absolutePath))
                return false;

            _entries.Remove((source, preset));
            FlushCore(removedKeys: new HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> { (source, preset) });
        }

        RemoveMetadataEntry(removed);

        OnChanged(source, preset, ProxyStoreChangeKind.Deleted);
        return true;
    }

    public void Touch(ProxyFingerprint source, ProxyPreset preset, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            nowUtc = nowUtc.ToUniversalTime();

        bool touched = false;
        lock (_lock)
        {
            if (_entries.TryGetValue((source, preset), out ProxyEntry? entry))
            {
                _entries[(source, preset)] = entry with { LastUsedUtc = nowUtc };
                _touchDirty = true;
                _touchDirtyKeys.Add((source, preset));
                touched = true;
            }
        }

        if (touched)
        {
            ScheduleTouchFlush();
            OnChanged(source, preset, ProxyStoreChangeKind.Touched);
        }
    }

    public long GetTotalBytes()
    {
        lock (_lock)
        {
            return _entries.Values
                .Where(static e => e.State is ProxyState.Ready or ProxyState.Stale or ProxyState.Failed)
                .Sum(static e => e.ProxyFileSizeBytes);
        }
    }

    public long GetTotalBytes(IReadOnlySet<string> sourceAbsolutePaths)
    {
        ArgumentNullException.ThrowIfNull(sourceAbsolutePaths);
        HashSet<string> normalized = [.. sourceAbsolutePaths.Select(ProxyFingerprint.NormalizeAbsolutePath)];

        lock (_lock)
        {
            return _entries.Values
                .Where(e => normalized.Contains(e.Source.AbsolutePath))
                .Where(static e => e.State is ProxyState.Ready or ProxyState.Stale or ProxyState.Failed)
                .Sum(static e => e.ProxyFileSizeBytes);
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            FlushCore();
        }

        return Task.CompletedTask;
    }

    public Task ReconcileAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string tmp in Directory.EnumerateFiles(StoreRootPath, "*", SearchOption.AllDirectories)
                         .Where(path => ProxyPathUtilities.IsGeneratedProxyTempPath(StoreRootPath, path))
                         .Where(IsOldEnoughToCleanGeneratedTemp))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryDelete(tmp);
            }

            HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> changedKeys = [];
            List<ProxyEntry> snapshot;
            lock (_lock)
            {
                changedKeys.UnionWith(AdoptSidecarsCore(cancellationToken));
                snapshot = [.. _entries.Values];
            }

            // Stat each tracked entry outside _lock: File.Exists / FileInfo.Length / FromFile
            // (symlink resolve) over the whole store would otherwise block the preview hot path
            // (TryGet/Touch/Enumerate) behind startup reconciliation. The mutations below re-acquire
            // _lock and re-validate each key against the current entry before acting.
            List<ProxyEntry> missing = [];
            List<ProxyEntry> changed = [];
            foreach (ProxyEntry entry in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path;
                try
                {
                    path = GetAbsolutePath(entry);
                }
                catch
                {
                    missing.Add(entry);
                    continue;
                }

                if (entry.State == ProxyState.Failed)
                    continue;

                if (!File.Exists(path))
                {
                    missing.Add(entry);
                    continue;
                }

                if (entry.State is ProxyState.Ready or ProxyState.Stale
                    && !HasValidReadyFile(entry, path))
                {
                    missing.Add(entry);
                    continue;
                }

                if (entry.State == ProxyState.Ready
                    && File.Exists(entry.Source.SourcePath)
                    && ProxyFingerprint.FromFile(entry.Source.SourcePath) != entry.Source)
                {
                    changed.Add(entry);
                }
            }

            List<ProxyEntry> removedEntries = [];
            List<ProxyEntry> changedEntries = [];
            HashSet<string> trackedProxyPaths;
            lock (_lock)
            {
                HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> removedKeys = [];
                foreach (ProxyEntry entry in missing)
                {
                    var key = (entry.Source, entry.Preset);
                    // Act only on entries unchanged since the unlocked scan; a concurrent
                    // Register/TryTransition/Touch supersedes our now-stale decision.
                    if (_entries.TryGetValue(key, out ProxyEntry? current) && current == entry)
                    {
                        _entries.Remove(key);
                        removedKeys.Add(key);
                        removedEntries.Add(entry);
                    }
                }

                foreach (ProxyEntry entry in changed)
                {
                    var key = (entry.Source, entry.Preset);
                    if (_entries.TryGetValue(key, out ProxyEntry? current) && current == entry)
                    {
                        _entries[key] = entry with
                        {
                            State = ProxyState.Stale,
                            LastUsedUtc = DateTime.UtcNow,
                        };
                        changedKeys.Add(key);
                        changedEntries.Add(entry);
                    }
                }

                if (removedKeys.Count > 0 || changedKeys.Count > 0)
                {
                    FlushCore(changedKeys, removedKeys);
                }

                trackedProxyPaths = CollectTrackedProxyPaths();
            }

            // Scan and delete orphaned proxy files outside the store lock: this walks the whole store
            // root and stats/deletes files, which would otherwise block UI paths (TryGet/Touch/
            // Enumerate) behind startup reconciliation. A just-generated proxy is younger than the
            // age threshold, so the snapshot going slightly stale cannot reclaim a live file.
            ReclaimOrphanProxyFiles(trackedProxyPaths, cancellationToken);

            foreach (ProxyEntry entry in removedEntries)
            {
                OnChanged(entry.Source, entry.Preset, ProxyStoreChangeKind.Deleted);
            }

            foreach (ProxyEntry entry in changedEntries)
            {
                OnChanged(entry.Source, entry.Preset, ProxyStoreChangeKind.StateChanged);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Reconcile is best-effort; serving known-good entries is safer than failing startup.
            s_logger.LogWarning(
                ex,
                "Proxy store reconciliation failed; stale entries and orphan files may remain until the next reconcile.");
        }

        return Task.CompletedTask;
    }

    internal string GetAbsolutePath(ProxyEntry entry)
    {
        return ProxyPathUtilities.ResolveRelativePath(StoreRootPath, entry.ProxyFileRelative);
    }

    private void LoadIndex()
    {
        if (!File.Exists(_indexPath))
            return;

        try
        {
            string json = File.ReadAllText(_indexPath);
            ProxyStoreIndex? index = JsonSerializer.Deserialize<ProxyStoreIndex>(json, s_jsonOptions);
            if (index?.Version != ProxyStoreIndex.CurrentVersion)
            {
                _entries.Clear();
                HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> adoptedKeys = AdoptSidecarsCore(CancellationToken.None);
                FlushCore(adoptedKeys);
                return;
            }

            foreach (ProxyEntry entry in index.Entries)
            {
                if (TryValidateEntry(entry))
                    _entries[(entry.Source, entry.Preset)] = entry;
            }
        }
        catch
        {
            _entries.Clear();
            HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> adoptedKeys = AdoptSidecarsCore(CancellationToken.None);
            FlushCore(adoptedKeys);
        }
    }

    private HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> AdoptSidecarsCore(CancellationToken cancellationToken)
    {
        HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> adoptedKeys = [];
        foreach (string metadataPath in Directory.EnumerateFiles(StoreRootPath, "meta.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (ProxyEntry entry in ReadMetadataEntries(metadataPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryValidateEntry(entry))
                    continue;

                string proxyPath;
                try
                {
                    proxyPath = GetAbsolutePath(entry);
                }
                catch
                {
                    continue;
                }

                if (!File.Exists(proxyPath))
                    continue;

                var key = (entry.Source, entry.Preset);
                if (!_entries.TryGetValue(key, out ProxyEntry? existing)
                    || ShouldAdoptSidecar(entry, existing))
                {
                    _entries[key] = entry;
                    adoptedKeys.Add(key);
                }
            }
        }

        return adoptedKeys;
    }

    private HashSet<string> CollectTrackedProxyPaths()
    {
        HashSet<string> tracked = [];
        foreach (ProxyEntry entry in _entries.Values)
        {
            try
            {
                tracked.Add(ProxyFingerprint.NormalizeAbsolutePath(GetAbsolutePath(entry)));
            }
            catch
            {
            }
        }

        return tracked;
    }

    private void ReclaimOrphanProxyFiles(HashSet<string> tracked, CancellationToken cancellationToken)
    {
        foreach (string file in Directory.EnumerateFiles(StoreRootPath, "*.mp4", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ProxyPathUtilities.IsGeneratedProxyTempPath(StoreRootPath, file))
                continue;

            // Only reclaim files that match the generated proxy naming scheme; a user who points the
            // store root at an existing media folder must not have unrelated *.mp4 files deleted just
            // because they are absent from index.json.
            if (!ProxyPathUtilities.IsGeneratedProxyFinalPath(StoreRootPath, file))
                continue;

            if (tracked.Contains(ProxyFingerprint.NormalizeAbsolutePath(file)))
                continue;

            // A just-generated proxy is moved into place before its index/sidecar entry is
            // written; skipping recent files avoids racing that window. Genuine orphans are
            // reclaimed once they age past the same threshold used for temp cleanup.
            if (!IsOldEnoughToCleanGeneratedTemp(file))
                continue;

            TryDelete(file);
        }
    }

    private static IEnumerable<ProxyEntry> ReadMetadataEntries(string metadataPath)
    {
        string json;
        try
        {
            json = File.ReadAllText(metadataPath);
        }
        catch
        {
            yield break;
        }

        ProxySourceMetadata? metadata = null;
        try
        {
            metadata = JsonSerializer.Deserialize<ProxySourceMetadata>(json, s_jsonOptions);
        }
        catch
        {
        }

        // A legacy single-ProxyEntry sidecar also deserializes as ProxySourceMetadata (Version and
        // Entries take their defaults), so only treat it as a wrapper when it actually carries
        // entries — otherwise fall through to the legacy ProxyEntry parse so recovery still adopts it.
        if (metadata is { Entries.Count: > 0 })
        {
            if (metadata.Version == ProxySourceMetadata.CurrentVersion)
            {
                foreach (ProxyEntry entry in metadata.Entries)
                {
                    if (entry.Source == metadata.Source)
                        yield return entry;
                }
            }

            yield break;
        }

        ProxyEntry? legacyEntry = null;
        try
        {
            legacyEntry = JsonSerializer.Deserialize<ProxyEntry>(json, s_jsonOptions);
        }
        catch
        {
        }

        if (legacyEntry != null)
            yield return legacyEntry;
    }

    // Intentionally holds _lock for the full read-merge-write. Moving the write outside _lock
    // (to unblock TryGet/Touch/Enumerate during flush) breaks two invariants the single-lock span
    // guarantees: (1) the disk read must happen under the cross-process index.lock so another
    // instance cannot write between our read and our write; (2) the _entries clear+refill from the
    // merged result is only safe with no concurrent in-process mutation — once _lock is released
    // during the write, a concurrent Register/Delete can land between the refill and the write, and
    // the refill re-adds a just-deleted key from disk while Phase 3's HashSet.Remove(key) clears a
    // concurrent degraded flush's pending addition, losing that change. Fixing (2) needs a
    // ref-counted pending structure plus an incremental _entries reconcile, which is high-risk for a
    // LOW-severity finding and not deterministically testable. The Touch debounce (1 s) and
    // DegradePersistence (pending replay on the next uncontended flush) mitigate the perf concern.
    private void FlushCore(
        IReadOnlySet<(ProxyFingerprint Source, ProxyPreset Preset)>? changedKeys = null,
        IReadOnlySet<(ProxyFingerprint Source, ProxyPreset Preset)>? removedKeys = null)
    {
        Directory.CreateDirectory(StoreRootPath);
        FileStream? indexLock = AcquireIndexLock(out Exception? lockFailure);
        if (indexLock is null)
        {
            DegradePersistence(changedKeys, removedKeys, lockFailure);
            return;
        }

        using (indexLock)
        {
            HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> effectiveRemoved = [.. _pendingRemoveKeys];
            if (removedKeys != null)
                effectiveRemoved.UnionWith(removedKeys);

            HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> effectiveChanged = [.. _pendingPersistKeys];
            if (changedKeys != null)
                effectiveChanged.UnionWith(changedKeys);
            effectiveChanged.ExceptWith(effectiveRemoved);

            Dictionary<(ProxyFingerprint Source, ProxyPreset Preset), ProxyEntry> merged = [];
            foreach (ProxyEntry entry in ReadIndexEntriesFromDisk())
            {
                var key = GetKey(entry);
                if (effectiveRemoved.Contains(key))
                    continue;

                merged[key] = entry;
            }

            HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> touchedKeys = [.. _touchDirtyKeys];
            foreach (var key in touchedKeys)
            {
                if (effectiveRemoved.Contains(key))
                    continue;

                if (merged.TryGetValue(key, out ProxyEntry? diskEntry)
                    && _entries.TryGetValue(key, out ProxyEntry? localEntry))
                {
                    DateTime lastUsedUtc = diskEntry.LastUsedUtc >= localEntry.LastUsedUtc
                        ? diskEntry.LastUsedUtc
                        : localEntry.LastUsedUtc;
                    merged[key] = diskEntry with { LastUsedUtc = lastUsedUtc };
                }
                else if (!effectiveChanged.Contains(key))
                {
                    // A touched key that is not yet on disk but is pending persistence (e.g. a
                    // registration degraded by transient lock contention) must survive this replay;
                    // the effectiveChanged pass below writes it. Only drop keys with no pending change.
                    _entries.Remove(key);
                }
            }

            foreach (var key in effectiveChanged)
            {
                if (_entries.TryGetValue(key, out ProxyEntry? entry))
                    merged[key] = entry;
            }

            _entries.Clear();
            foreach (ProxyEntry entry in merged.Values)
            {
                _entries[GetKey(entry)] = entry;
            }

            var index = new ProxyStoreIndex { Entries = [.. merged.Values] };
            string json = JsonSerializer.Serialize(index, s_jsonOptions);
            string tmp = Path.Combine(
                StoreRootPath,
                $"{Path.GetFileName(_indexPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(tmp, json);
                File.Move(tmp, _indexPath, overwrite: true);
                foreach (var key in touchedKeys)
                {
                    _touchDirtyKeys.Remove(key);
                }

                _touchDirty = _touchDirtyKeys.Count > 0;
                _pendingPersistKeys.Clear();
                _pendingRemoveKeys.Clear();
                _persistenceDegraded = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A durable-write failure (transient I/O or a store root without write permission)
                // degrades like a contended lock instead of throwing out of Register/TryTransition/
                // FlushAsync; the pending sets are left populated (not cleared above) so the change
                // replays on the next flush.
                DegradePersistence(changedKeys, removedKeys, ex);
            }
            finally
            {
                TryDelete(tmp);
            }
        }
    }

    private void DegradePersistence(
        IReadOnlySet<(ProxyFingerprint Source, ProxyPreset Preset)>? changedKeys,
        IReadOnlySet<(ProxyFingerprint Source, ProxyPreset Preset)>? removedKeys,
        Exception? cause = null)
    {
        bool firstDegradation = !_persistenceDegraded;
        _persistenceDegraded = true;
        if (removedKeys != null)
        {
            foreach (var key in removedKeys)
            {
                _pendingRemoveKeys.Add(key);
                _pendingPersistKeys.Remove(key);
            }
        }

        if (changedKeys != null)
        {
            foreach (var key in changedKeys)
            {
                // A re-Register after a Delete (both degraded) must supersede the pending removal.
                _pendingRemoveKeys.Remove(key);
                _pendingPersistKeys.Add(key);
            }
        }

        // Log only on the transition into the degraded state: the 1 s touch-flush loop retries a
        // persistent fault every second and a per-attempt warning would flood the log. The flag is
        // reset by the next successful flush, so each degradation episode logs exactly once.
        if (!firstDegradation)
            return;

        if (cause is null)
        {
            s_logger.LogWarning(
                "Proxy index lock at '{LockPath}' is contended; skipping durable persistence and serving in-memory state (read-only degradation).",
                _indexLockPath);
        }
        else
        {
            s_logger.LogWarning(
                cause,
                "Proxy index write to '{IndexPath}' failed; skipping durable persistence and serving in-memory state until a later flush succeeds.",
                _indexPath);
        }
    }

    private async Task FlushTouchesAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            lock (_lock)
            {
                if (!_touchDirty)
                    return;

                FlushCore();
            }

            Interlocked.Exchange(ref _touchFlushFaulted, 0);
        }
        catch (Exception ex)
        {
            // FlushCore degrades I/O and permission faults internally, so anything landing here is
            // unexpected (bug-class). Log once per failure streak — the finally block reschedules
            // this loop every second and a per-attempt error would flood the log.
            if (Interlocked.Exchange(ref _touchFlushFaulted, 1) == 0)
            {
                s_logger.LogError(
                    ex,
                    "Deferred proxy touch flush failed unexpectedly; LastUsedUtc updates stay pending and will retry every second.");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _touchFlushScheduled, 0);
            lock (_lock)
            {
                if (_touchDirty)
                    ScheduleTouchFlush();
            }
        }
    }

    private void ScheduleTouchFlush()
    {
        if (Interlocked.Exchange(ref _touchFlushScheduled, 1) == 0)
            _ = FlushTouchesAsync();
    }

    private void RemoveMetadataEntry(ProxyEntry removed)
    {
        try
        {
            string metadataPath = Path.Combine(Path.GetDirectoryName(GetAbsolutePath(removed))!, "meta.json");
            if (!File.Exists(metadataPath))
                return;

            string json = File.ReadAllText(metadataPath);
            ProxySourceMetadata? metadata = JsonSerializer.Deserialize<ProxySourceMetadata>(json, s_jsonOptions);
            if (metadata == null)
                return;

            ProxyEntry[] entries =
            [
                .. metadata.Entries.Where(entry => entry.Source != removed.Source || entry.Preset != removed.Preset)
            ];

            if (entries.Length == 0)
            {
                File.Delete(metadataPath);
                return;
            }

            metadata = metadata with { Entries = [.. entries] };
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, s_jsonOptions));
        }
        catch
        {
        }
    }

    private IEnumerable<ProxyEntry> ReadIndexEntriesFromDisk()
    {
        if (!File.Exists(_indexPath))
            yield break;

        ProxyStoreIndex? index;
        try
        {
            index = JsonSerializer.Deserialize<ProxyStoreIndex>(File.ReadAllText(_indexPath), s_jsonOptions);
        }
        catch
        {
            yield break;
        }

        if (index?.Version != ProxyStoreIndex.CurrentVersion)
            yield break;

        foreach (ProxyEntry entry in index.Entries)
        {
            if (TryValidateEntry(entry))
                yield return entry;
        }
    }

    private FileStream? AcquireIndexLock(out Exception? failure)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                failure = null;
                return new FileStream(
                    _indexLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (attempt < _lockAcquireMaxAttempts)
            {
                Thread.Sleep(10);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // UnauthorizedAccessException (store root without write permission) is not
                // transient, so it skips the retry loop and degrades immediately. Only the
                // permission fault is surfaced — an IOException here is ordinary lock
                // contention, which the null-cause degradation message already describes.
                failure = ex is UnauthorizedAccessException ? ex : null;
                return null;
            }
        }
    }

    private static (ProxyFingerprint Source, ProxyPreset Preset) GetKey(ProxyEntry entry)
    {
        return (entry.Source, entry.Preset);
    }

    private static bool ShouldAdoptSidecar(ProxyEntry sidecarEntry, ProxyEntry existingEntry)
    {
        return sidecarEntry.State is ProxyState.Ready or ProxyState.Stale
            && existingEntry.State is not (ProxyState.Ready or ProxyState.Stale);
    }

    private bool TryValidateEntry(ProxyEntry entry)
    {
        try
        {
            string path = GetAbsolutePath(entry);
            return entry.State switch
            {
                ProxyState.Ready or ProxyState.Stale => File.Exists(path) && HasValidReadyFile(entry, path),
                _ => true,
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool HasValidReadyFile(ProxyEntry entry, string absolutePath)
    {
        if (entry.ProxyFileSizeBytes <= 0
            || entry.OriginalLogicalFrameSize.Width <= 0
            || entry.OriginalLogicalFrameSize.Height <= 0
            || entry.ProxyDecodedFrameSize.Width <= 0
            || entry.ProxyDecodedFrameSize.Height <= 0)
        {
            return false;
        }

        try
        {
            return new FileInfo(absolutePath).Length == entry.ProxyFileSizeBytes;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsOldEnoughToCleanGeneratedTemp(string path)
    {
        try
        {
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= s_generatedTempCleanupMinAge;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeleteProxyFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnChanged(ProxyFingerprint source, ProxyPreset preset, ProxyStoreChangeKind kind)
    {
        if (Changed is not { } handlers)
            return;

        var args = new ProxyStoreChangedEventArgs
        {
            Source = source,
            Preset = preset,
            Kind = kind,
        };
        // Changed is an invalidation notification, not part of the mutation result: a throwing
        // subscriber must not fault Register/Delete/TryTransition (e.g. making a committed Ready
        // entry look like a failed registration to the generator) nor starve other subscribers.
        foreach (EventHandler<ProxyStoreChangedEventArgs> handler in
                 Delegate.EnumerateInvocationList(handlers))
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                s_logger.LogError(
                    ex,
                    "A ProxyStore.Changed subscriber threw for {Source} ({Preset}, {Kind}).",
                    source.AbsolutePath,
                    preset,
                    kind);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
