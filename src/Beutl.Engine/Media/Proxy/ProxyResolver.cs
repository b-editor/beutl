using System.Collections.Concurrent;

namespace Beutl.Media.Proxy;

public sealed class ProxyResolver : IProxyResolver
{
    private readonly IProxyStore _store;
    private readonly ConcurrentDictionary<string, int> _pins = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _sourceVersions = new(StringComparer.Ordinal);

    public ProxyResolver(IProxyStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _store.Changed += OnStoreChanged;
    }

    public long GetSourceVersion(string sourceAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAbsolutePath);
        string key = ProxyFingerprint.NormalizeAbsolutePath(sourceAbsolutePath);
        return _sourceVersions.TryGetValue(key, out long version) ? version : 0;
    }

    public ProxyResolution? Resolve(Uri sourceUri, ProxyPreset preferredPreset)
    {
        ArgumentNullException.ThrowIfNull(sourceUri);
        if (!sourceUri.IsFile)
            return null;

        if (!ProxyFingerprint.TryFromFile(sourceUri.LocalPath, out ProxyFingerprint fingerprint))
            return null;

        foreach (ProxyPreset preset in EnumeratePresetsByPreference(preferredPreset))
        {
            if (TryResolve(fingerprint, preset) is { } resolution)
                return resolution;
        }

        return null;
    }

    // Prefer the densest (highest-fidelity) Ready proxy at or above the requested
    // fidelity, then fall back to the densest available below it. A deliberately
    // generated higher-fidelity per-clip proxy therefore wins over the global default
    // instead of being silently downgraded to it.
    private static IEnumerable<ProxyPreset> EnumeratePresetsByPreference(ProxyPreset preferredPreset)
    {
        // An undefined preferredPreset degrades to plain densest-first (requestedScale 0).
        float requestedScale = ProxyPresetDefinitions.All.TryGetValue(preferredPreset, out ProxyEncodeParameters parameters)
            ? parameters.Scale
            : 0f;
        return ProxyPresetDefinitions.All.Keys
            .OrderByDescending(preset => ScaleOf(preset) >= requestedScale)
            .ThenByDescending(ScaleOf);
    }

    private static float ScaleOf(ProxyPreset preset)
    {
        return ProxyPresetDefinitions.Get(preset).Scale;
    }

    /// <summary>
    /// Takes a transient, reference-counted decode-lifetime safety pin on a resolved
    /// proxy file so eviction cannot delete it while a MediaReader is decoding it.
    /// Dispose the returned handle to release the reference.
    /// This is NOT the future user-facing "do-not-evict" pin (FR-018a); that is a
    /// separate, persistent, user-driven concept — do not conflate the two.
    /// </summary>
    public IDisposable Pin(ProxyResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        string path = resolution.AbsoluteProxyFilePath;
        _pins.AddOrUpdate(path, 1, static (_, count) => checked(count + 1));
        return new PinHandle(this, path);
    }

    /// <summary>
    /// True while at least one transient decode-lifetime safety pin (see <see cref="Pin"/>)
    /// is held for the proxy file. Unrelated to FR-018a's future persistent user pin.
    /// </summary>
    public bool IsPinned(string absoluteProxyFilePath)
    {
        return _pins.TryGetValue(Path.GetFullPath(absoluteProxyFilePath), out int count) && count > 0;
    }

    private ProxyResolution? TryResolve(ProxyFingerprint fingerprint, ProxyPreset preset)
    {
        ProxyEntry? entry = _store.TryGet(fingerprint, preset);
        if (entry is not { State: ProxyState.Ready })
            return null;

        if (!TryGetAbsolutePath(entry, out string absolutePath))
            return null;

        if (!TryGetProxyFileSize(absolutePath, out long fileSize))
            return null;

        if (entry.ProxyFileSizeBytes <= 0
            || entry.OriginalLogicalFrameSize.Width <= 0
            || entry.OriginalLogicalFrameSize.Height <= 0
            || entry.ProxyDecodedFrameSize.Width <= 0
            || entry.ProxyDecodedFrameSize.Height <= 0)
        {
            return null;
        }

        if (fileSize != entry.ProxyFileSizeBytes)
            return null;

        _store.Touch(fingerprint, preset, DateTime.UtcNow);
        return new ProxyResolution(
            absolutePath,
            fingerprint,
            preset,
            entry.OriginalLogicalFrameSize,
            entry.ProxyDecodedFrameSize);
    }

    private bool TryGetAbsolutePath(ProxyEntry entry, out string absolutePath)
    {
        return ProxyPathUtilities.TryResolveRelativePath(_store.StoreRootPath, entry.ProxyFileRelative, out absolutePath);
    }

    private static bool TryGetProxyFileSize(string absolutePath, out long fileSize)
    {
        try
        {
            var info = new FileInfo(absolutePath);
            if (!info.Exists)
            {
                fileSize = 0;
                return false;
            }

            fileSize = info.Length;
            return true;
        }
        catch
        {
            fileSize = 0;
            return false;
        }
    }

    private void OnStoreChanged(object? sender, ProxyStoreChangedEventArgs e)
    {
        if (e.Kind is ProxyStoreChangeKind.Registered
            or ProxyStoreChangeKind.StateChanged
            or ProxyStoreChangeKind.Deleted)
        {
            // Bump only the changed source's version (e.Source is already normalized)
            // so unrelated proxied sources are not invalidated.
            _sourceVersions.AddOrUpdate(e.Source.AbsolutePath, 1, static (_, version) => version + 1);
        }
    }

    private void Unpin(string path)
    {
        _pins.AddOrUpdate(
            path,
            0,
            static (_, count) => Math.Max(0, count - 1));

        if (_pins.TryGetValue(path, out int count) && count <= 0)
            _pins.TryRemove(path, out _);
    }

    // Disposable release token for a transient decode-lifetime safety pin (see Pin).
    // Not related to FR-018a's future persistent user pin.
    private sealed class PinHandle(ProxyResolver resolver, string path) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                resolver.Unpin(path);
        }
    }
}
