using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Logging;

using Microsoft.Extensions.Logging;

using ProxyKey = (Beutl.Media.Proxy.ProxyFingerprint Source, Beutl.Media.Proxy.ProxyPreset Preset);

namespace Beutl.Media.Proxy;

public sealed partial class ProxyStore : IProxyStore
{
    // A live encode keeps its *.tmp mtime fresh, so the age check spares an active file; the wide margin covers a
    // peer instance's long encode on a shared store whose mtime lags.
    private static readonly TimeSpan s_generatedTempCleanupMinAge = TimeSpan.FromHours(24);

    private static readonly ILogger s_logger = Log.CreateLogger<ProxyStore>();

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Lock _lock = new();
    private readonly Dictionary<ProxyKey, ProxyEntry> _entries = [];
    private readonly Dictionary<string, int> _filePins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProxyEntry> _retiredFiles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reclaimingPaths = new(StringComparer.Ordinal);

    private readonly string _indexPath;
    private int _touchFlushScheduled;
    private int _touchFlushFaulted;
    private bool _touchDirty;
    private bool _persistenceDegraded;

    public ProxyStore(string storeRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRootPath);
        StoreRootPath = Path.GetFullPath(storeRootPath);
        _indexPath = Path.Combine(StoreRootPath, "index.json");
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
            string path = ProxyFingerprint.NormalizeAbsolutePath(GetAbsolutePath(entry));
            if (_reclaimingPaths.Contains(path))
                throw new IOException("The proxy file is being reclaimed.");

            ProxyEntry? previous = _entries.GetValueOrDefault(key);
            _entries[key] = entry;
            _retiredFiles.Remove(path);
            if (previous != null)
            {
                string previousPath = ProxyFingerprint.NormalizeAbsolutePath(GetAbsolutePath(previous));
                if (previousPath != path && !IsProxyFileReferenced(previousPath))
                    _retiredFiles[previousPath] = previous;
            }

            FlushCore();
        }

        OnChanged(entry.Source, entry.Preset, ProxyStoreChangeKind.Registered);
        ReclaimRetiredProxyFiles();
    }

    // Pin acquisition, reference replacement and deletion claims share _lock. A resolution obtained
    // before a replacement cannot acquire a new pin on a retired generation and race its deletion.
    internal IDisposable Pin(ProxyResolution resolution)
    {
        string path = ProxyFingerprint.NormalizeAbsolutePath(resolution.AbsoluteProxyFilePath);
        lock (_lock)
        {
            ProxyEntry? entry = _entries.GetValueOrDefault((resolution.Source, resolution.Preset));
            if (entry is not { State: ProxyState.Ready }
                || ProxyFingerprint.NormalizeAbsolutePath(GetAbsolutePath(entry)) != path)
            {
                throw new InvalidOperationException("The resolved proxy generation is no longer ready.");
            }

            _filePins[path] = checked(_filePins.GetValueOrDefault(path) + 1);
        }

        return new FilePin(this, path);
    }

    internal bool IsPinned(string path)
    {
        path = ProxyFingerprint.NormalizeAbsolutePath(path);
        lock (_lock)
            return _filePins.GetValueOrDefault(path) > 0;
    }

    private void Unpin(string path)
    {
        bool reclaim;
        lock (_lock)
        {
            int count = _filePins[path];
            if (count > 1)
            {
                _filePins[path] = count - 1;
                return;
            }

            _filePins.Remove(path);
            reclaim = _retiredFiles.ContainsKey(path);
        }

        // Reader disposal can run on the UI or finalizer thread; filesystem cleanup runs separately.
        if (reclaim)
            _ = Task.Run(ReclaimRetiredProxyFiles);
    }

    private sealed class FilePin(ProxyStore store, string path) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                store.Unpin(path);
        }
    }

    private void ReclaimRetiredProxyFiles()
    {
        string[] paths;
        lock (_lock)
            paths = [.. _retiredFiles.Keys];

        foreach (string path in paths)
            TryReclaimProxyFile(path);
    }

    private bool TryReclaimProxyFile(string path)
    {
        path = ProxyFingerprint.NormalizeAbsolutePath(path);
        lock (_lock)
        {
            // A degraded flush may still leave the old generation referenced by index.json. Keep it
            // until a later successful flush, even after its last reader has released the pin.
            if (_persistenceDegraded || _filePins.GetValueOrDefault(path) > 0
                || _reclaimingPaths.Contains(path) || IsProxyFileReferenced(path))
            {
                return false;
            }

            _reclaimingPaths.Add(path);
        }

        // Do not hold _lock over unbounded filesystem I/O. Register rejects this claimed path and
        // Pin validates the current entry, so neither can start using the file while it is deleted.
        bool deleted = TryDeleteProxyFile(path);
        lock (_lock)
        {
            _reclaimingPaths.Remove(path);
            if (deleted)
                _retiredFiles.Remove(path);
        }

        return deleted;
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
            FlushCore();
        }

        OnChanged(source, preset, ProxyStoreChangeKind.StateChanged);
        return true;
    }

    public bool Delete(ProxyFingerprint source, ProxyPreset preset)
    {
        ProxyEntry removed;
        string proxyPath;
        lock (_lock)
        {
            if (!_entries.TryGetValue((source, preset), out ProxyEntry? existing))
                return false;

            removed = existing;
            proxyPath = GetAbsolutePath(removed);
            _entries.Remove((source, preset));
            if (!IsProxyFileReferenced(proxyPath))
                _retiredFiles[ProxyFingerprint.NormalizeAbsolutePath(proxyPath)] = removed;
            FlushCore();
        }

        // Delete outside _lock, which preview reads contend on, since File.Delete can stall on a network share.
        // Re-check first: a re-registration or another entry may still own the file.
        if (!IsProxyFileReferenced(proxyPath))
        {
            TryReclaimProxyFile(proxyPath);
            RemoveMetadataEntry(removed);
        }

        OnChanged(source, preset, ProxyStoreChangeKind.Deleted);
        return true;
    }

    private bool IsProxyFileReferenced(string absoluteProxyPath)
    {
        string normalized = ProxyFingerprint.NormalizeAbsolutePath(absoluteProxyPath);

        lock (_lock)
        {
            foreach (ProxyEntry entry in _entries.Values)
            {
                string candidate = ProxyFingerprint.NormalizeAbsolutePath(GetAbsolutePath(entry));
                if (string.Equals(candidate, normalized, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
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
            return _entries.Values.Concat(_retiredFiles.Values)
                .Where(static e => ProxyEntryChecks.CountsTowardStoreSize(e.State))
                .Sum(static e => e.ProxyFileSizeBytes);
        }
    }

    public long GetTotalBytes(IReadOnlySet<string> sourceAbsolutePaths)
    {
        ArgumentNullException.ThrowIfNull(sourceAbsolutePaths);
        HashSet<string> normalized = [.. sourceAbsolutePaths.Select(ProxyFingerprint.NormalizeAbsolutePath)];

        lock (_lock)
        {
            return _entries.Values.Concat(_retiredFiles.Values)
                .Where(e => normalized.Contains(e.Source.AbsolutePath))
                .Where(static e => ProxyEntryChecks.CountsTowardStoreSize(e.State))
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

        ReclaimRetiredProxyFiles();
        return Task.CompletedTask;
    }

    internal string GetAbsolutePath(ProxyEntry entry)
    {
        return ProxyPathUtilities.ResolveRelativePath(StoreRootPath, entry.ProxyFileRelative);
    }

    private static ProxyKey GetKey(ProxyEntry entry)
    {
        return (entry.Source, entry.Preset);
    }

    private bool TryValidateEntry(ProxyEntry entry)
    {
        // A path-key fingerprint (ProxyFingerprint.ForPathKey) carries FileSizeBytes == 0 and exists only
        // for AbsolutePath-based lookup, never persistence. Reject it at the store boundary so accidentally
        // registering one fails loudly here instead of silently storing a fingerprint no real entry equals.
        if (entry.Source.FileSizeBytes <= 0)
            return false;

        try
        {
            string path = GetAbsolutePath(entry);
            return entry.State switch
            {
                ProxyState.Ready or ProxyState.Stale => ProxyEntryChecks.IsValidReadyFile(entry, path),
                _ => true,
            };
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
