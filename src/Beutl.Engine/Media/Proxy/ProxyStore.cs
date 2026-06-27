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

    public ProxyStore(string storeRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRootPath);
        StoreRootPath = Path.GetFullPath(storeRootPath);
        _indexPath = Path.Combine(StoreRootPath, "index.json");
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
        ValidateRelativePath(entry.ProxyFileRelative);

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
            if (_entries.Remove((source, preset), out removed))
            {
                FlushCore();
            }
        }

        if (removed == null)
            return false;

        string absolutePath = GetAbsolutePath(removed);
        try
        {
            if (File.Exists(absolutePath))
                File.Delete(absolutePath);
        }
        catch
        {
            // The index already stopped serving this proxy. A later reconcile can clean the file up.
        }

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
                FlushCore();
                touched = true;
            }
        }

        if (touched)
            OnChanged(source, preset, ProxyStoreChangeKind.Touched);
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
            foreach (string tmp in Directory.EnumerateFiles(StoreRootPath, "*.tmp", SearchOption.AllDirectories))
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
                    string path = GetAbsolutePath(entry);
                    if (!File.Exists(path))
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
                    FlushCore();
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
        string relative = entry.ProxyFileRelative.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(StoreRootPath, relative));
    }

    private void LoadIndex()
    {
        if (!File.Exists(_indexPath))
            return;

        try
        {
            string json = File.ReadAllText(_indexPath);
            ProxyStoreIndex? index = JsonSerializer.Deserialize<ProxyStoreIndex>(json, s_jsonOptions);
            if (index is not { Version: 1 })
            {
                AdoptSidecarsCore(CancellationToken.None);
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
            if (_entries.Count > 0)
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

                string proxyPath = GetAbsolutePath(entry);
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

        if (metadata is { Version: 1 })
        {
            foreach (ProxyEntry entry in metadata.Entries)
            {
                if (entry.Source == metadata.Source)
                    yield return entry;
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

    private void FlushCore()
    {
        Directory.CreateDirectory(StoreRootPath);
        var index = new ProxyStoreIndex { Entries = [.. _entries.Values] };
        string json = JsonSerializer.Serialize(index, s_jsonOptions);
        string tmp = _indexPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _indexPath, overwrite: true);
    }

    private static void ValidateRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathFullyQualified(relativePath) || relativePath.StartsWith('/'))
            throw new ArgumentException("Proxy path must be relative.", nameof(relativePath));
    }

    private static bool TryValidateEntry(ProxyEntry entry)
    {
        try
        {
            ValidateRelativePath(entry.ProxyFileRelative);
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
