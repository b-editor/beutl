using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Logging;

using Microsoft.Extensions.Logging;

namespace Beutl.Media.Proxy;

public sealed class ProxyStore : IProxyStore
{
    private const int DefaultLockAcquireMaxAttempts = 200;

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

    // Entries whose recorded source file is no longer present at its path — candidates for
    // relink (the source may have merely moved) or purge. This never mutates the store or
    // transitions the entries; the caller decides what to do with each.
    public IReadOnlyList<ProxyEntry> EnumerateEntriesWithMissingSource()
    {
        lock (_lock)
        {
            return [.. _entries.Values.Where(static e => !SourceFileExists(e.Source))];
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
                GeneratedAtUtc = DateTime.UtcNow,
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

    // Re-key every entry of a moved/renamed source from its old fingerprint to a new one,
    // so the existing proxy files are adopted without regeneration. The proxy files are left
    // in place; only the index and sidecar metadata are rewritten. Presets that already exist
    // under the new fingerprint are left untouched (no file is clobbered). Returns true if at
    // least one entry was relinked.
    public bool Relink(ProxyFingerprint oldSource, ProxyFingerprint newSource)
    {
        if (string.IsNullOrEmpty(oldSource.AbsolutePath) || string.IsNullOrEmpty(newSource.AbsolutePath))
            return false;

        if (oldSource == newSource)
            return false;

        List<ProxyEntry> relinked = [];
        List<(ProxyFingerprint Source, ProxyPreset Preset)> vacated = [];
        lock (_lock)
        {
            List<KeyValuePair<(ProxyFingerprint Source, ProxyPreset Preset), ProxyEntry>> candidates =
            [
                .. _entries.Where(pair => pair.Key.Source == oldSource)
            ];

            HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> changedKeys = [];
            HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> removedKeys = [];
            foreach (KeyValuePair<(ProxyFingerprint Source, ProxyPreset Preset), ProxyEntry> pair in candidates)
            {
                var newKey = (newSource, pair.Key.Preset);
                if (_entries.ContainsKey(newKey))
                    continue;

                ProxyEntry updated = pair.Value with { Source = newSource };
                _entries.Remove(pair.Key);
                _entries[newKey] = updated;
                changedKeys.Add(newKey);
                removedKeys.Add(pair.Key);
                relinked.Add(updated);
                vacated.Add(pair.Key);
            }

            if (relinked.Count == 0)
                return false;

            RewriteRelinkedMetadata(relinked, newSource);
            FlushCore(changedKeys, removedKeys);
        }

        foreach ((ProxyFingerprint Source, ProxyPreset Preset) key in vacated)
        {
            OnChanged(key.Source, key.Preset, ProxyStoreChangeKind.Deleted);
        }

        foreach (ProxyEntry entry in relinked)
        {
            OnChanged(entry.Source, entry.Preset, ProxyStoreChangeKind.Registered);
        }

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

            List<ProxyEntry> missing = [];
            List<ProxyEntry> changed = [];
            HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> changedKeys = [];
            lock (_lock)
            {
                changedKeys.UnionWith(AdoptSidecarsCore(cancellationToken));

                foreach (ProxyEntry entry in _entries.Values)
                {
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
                        && File.Exists(entry.Source.AbsolutePath)
                        && ProxyFingerprint.FromFile(entry.Source.AbsolutePath) != entry.Source)
                    {
                        changed.Add(entry);
                    }
                }

                foreach (ProxyEntry entry in missing)
                {
                    _entries.Remove((entry.Source, entry.Preset));
                }

                foreach (ProxyEntry entry in changed)
                {
                    _entries[(entry.Source, entry.Preset)] = entry with
                    {
                        State = ProxyState.Stale,
                        LastUsedUtc = DateTime.UtcNow,
                    };
                    changedKeys.Add((entry.Source, entry.Preset));
                }

                if (missing.Count > 0 || changedKeys.Count > 0)
                {
                    HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> removedKeys =
                    [
                        .. missing.Select(static entry => (entry.Source, entry.Preset))
                    ];
                    FlushCore(changedKeys, removedKeys);
                }

                ReclaimOrphanProxyFilesCore(cancellationToken);
            }

            foreach (ProxyEntry entry in missing)
            {
                OnChanged(entry.Source, entry.Preset, ProxyStoreChangeKind.Deleted);
            }

            foreach (ProxyEntry entry in changed)
            {
                OnChanged(entry.Source, entry.Preset, ProxyStoreChangeKind.StateChanged);
            }
        }
        catch
        {
            // Reconcile is best-effort; serving known-good entries is safer than failing startup.
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

    private void ReclaimOrphanProxyFilesCore(CancellationToken cancellationToken)
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

        foreach (string file in Directory.EnumerateFiles(StoreRootPath, "*.mp4", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ProxyPathUtilities.IsGeneratedProxyTempPath(StoreRootPath, file))
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

        if (metadata != null)
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

    private void FlushCore(
        IReadOnlySet<(ProxyFingerprint Source, ProxyPreset Preset)>? changedKeys = null,
        IReadOnlySet<(ProxyFingerprint Source, ProxyPreset Preset)>? removedKeys = null)
    {
        Directory.CreateDirectory(StoreRootPath);
        FileStream? indexLock = AcquireIndexLock();
        if (indexLock is null)
        {
            DegradePersistence(changedKeys, removedKeys);
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
                else
                {
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
            finally
            {
                TryDelete(tmp);
            }
        }
    }

    private void DegradePersistence(
        IReadOnlySet<(ProxyFingerprint Source, ProxyPreset Preset)>? changedKeys,
        IReadOnlySet<(ProxyFingerprint Source, ProxyPreset Preset)>? removedKeys)
    {
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
                if (!_pendingRemoveKeys.Contains(key))
                    _pendingPersistKeys.Add(key);
            }
        }

        s_logger.LogWarning(
            "Proxy index lock at '{LockPath}' is contended; skipping durable persistence and serving in-memory state (read-only degradation).",
            _indexLockPath);
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
        }
        catch
        {
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

    private void RewriteRelinkedMetadata(IReadOnlyList<ProxyEntry> relinked, ProxyFingerprint newSource)
    {
        foreach (IGrouping<string, ProxyEntry> group in relinked
                     .Select(entry => (Entry: entry, Directory: TryGetMetadataDirectory(entry)))
                     .Where(pair => pair.Directory != null)
                     .GroupBy(pair => pair.Directory!, pair => pair.Entry))
        {
            string metadataPath = Path.Combine(group.Key, "meta.json");
            try
            {
                var metadata = new ProxySourceMetadata
                {
                    Source = newSource,
                    Entries = [.. group],
                };
                File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, s_jsonOptions));
            }
            catch
            {
            }
        }
    }

    private string? TryGetMetadataDirectory(ProxyEntry entry)
    {
        try
        {
            return Path.GetDirectoryName(GetAbsolutePath(entry));
        }
        catch
        {
            return null;
        }
    }

    private static bool SourceFileExists(ProxyFingerprint source)
    {
        try
        {
            return !string.IsNullOrEmpty(source.AbsolutePath) && File.Exists(source.AbsolutePath);
        }
        catch
        {
            return false;
        }
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

    private FileStream? AcquireIndexLock()
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
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
            catch (IOException)
            {
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
        Changed?.Invoke(this, new ProxyStoreChangedEventArgs
        {
            Source = source,
            Preset = preset,
            Kind = kind,
        });
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
