using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Media.Proxy;

public sealed class ProxyStore : IProxyStore
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Lock _lock = new();
    private readonly Dictionary<(ProxyFingerprint Source, ProxyPreset Preset), ProxyEntry> _entries = [];
    private readonly string _indexPath;
    private readonly string _indexLockPath;
    private int _touchFlushScheduled;
    private bool _touchDirty;

    public ProxyStore(string storeRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRootPath);
        StoreRootPath = Path.GetFullPath(storeRootPath);
        _indexPath = Path.Combine(StoreRootPath, "index.json");
        _indexLockPath = Path.Combine(StoreRootPath, "index.lock");
        Directory.CreateDirectory(StoreRootPath);
        LoadIndex();
    }

    public string StoreRootPath { get; }

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
            _entries[(entry.Source, entry.Preset)] = entry;
            FlushCore();
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
            _entries[(source, preset)] = updated;
            FlushCore();
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
            FlushCore(new HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> { (source, preset) });
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
                         .Where(path => ProxyPathUtilities.IsGeneratedProxyTempPath(StoreRootPath, path)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryDelete(tmp);
            }

            List<ProxyEntry> missing = [];
            List<ProxyEntry> changed = [];
            lock (_lock)
            {
                AdoptSidecarsCore(cancellationToken);

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
                }

                if (missing.Count > 0 || changed.Count > 0)
                {
                    HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> removedKeys =
                    [
                        .. missing.Select(static entry => (entry.Source, entry.Preset))
                    ];
                    FlushCore(removedKeys);
                }
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
                AdoptSidecarsCore(CancellationToken.None);
                FlushCore();
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
            AdoptSidecarsCore(CancellationToken.None);
            FlushCore();
        }
    }

    private void AdoptSidecarsCore(CancellationToken cancellationToken)
    {
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

                _entries.TryAdd((entry.Source, entry.Preset), entry);
            }
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

    private void FlushCore(IReadOnlySet<(ProxyFingerprint Source, ProxyPreset Preset)>? removedKeys = null)
    {
        Directory.CreateDirectory(StoreRootPath);
        using FileStream indexLock = AcquireIndexLock();

        foreach (ProxyEntry entry in ReadIndexEntriesFromDisk())
        {
            var key = (entry.Source, entry.Preset);
            if (removedKeys?.Contains(key) == true)
                continue;

            _entries.TryAdd(key, entry);
        }

        var index = new ProxyStoreIndex { Entries = [.. _entries.Values] };
        string json = JsonSerializer.Serialize(index, s_jsonOptions);
        string tmp = Path.Combine(
            StoreRootPath,
            $"{Path.GetFileName(_indexPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, _indexPath, overwrite: true);
            _touchDirty = false;
        }
        finally
        {
            TryDelete(tmp);
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

    private FileStream AcquireIndexLock()
    {
        const int maxAttempts = 200;
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
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(10);
            }
        }
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
