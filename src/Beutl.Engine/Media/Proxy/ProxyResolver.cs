using System.Collections.Concurrent;

namespace Beutl.Media.Proxy;

public sealed class ProxyResolver : IProxyResolver
{
    private static readonly ProxyPreset[] s_fallbackOrder =
    [
        ProxyPreset.Half,
        ProxyPreset.Quarter,
        ProxyPreset.Eighth,
    ];

    private readonly IProxyStore _store;
    private readonly ConcurrentDictionary<string, int> _pins = new(StringComparer.Ordinal);
    private long _version;

    public ProxyResolver(IProxyStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _store.Changed += OnStoreChanged;
    }

    public long Version => Interlocked.Read(ref _version);

    public ProxyResolution? Resolve(Uri sourceUri, ProxyPreset preferredPreset)
    {
        ArgumentNullException.ThrowIfNull(sourceUri);
        if (!sourceUri.IsFile)
            return null;

        if (!ProxyFingerprint.TryFromFile(sourceUri.LocalPath, out ProxyFingerprint fingerprint))
            return null;

        if (TryResolve(fingerprint, preferredPreset) is { } exact)
            return exact;

        foreach (ProxyPreset preset in s_fallbackOrder)
        {
            if (preset == preferredPreset)
                continue;

            if (TryResolve(fingerprint, preset) is { } fallback)
                return fallback;
        }

        return null;
    }

    public IDisposable Pin(ProxyResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        string path = resolution.AbsoluteProxyFilePath;
        _pins.AddOrUpdate(path, 1, static (_, count) => checked(count + 1));
        return new PinHandle(this, path);
    }

    public bool IsPinned(string absoluteProxyFilePath)
    {
        return _pins.TryGetValue(Path.GetFullPath(absoluteProxyFilePath), out int count) && count > 0;
    }

    private ProxyResolution? TryResolve(ProxyFingerprint fingerprint, ProxyPreset preset)
    {
        ProxyEntry? entry = _store.TryGet(fingerprint, preset);
        if (entry is not { State: ProxyState.Ready })
            return null;

        string absolutePath = GetAbsolutePath(entry);
        if (!File.Exists(absolutePath))
            return null;

        long fileSize = new FileInfo(absolutePath).Length;
        if (entry.ProxyFileSizeBytes > 0 && fileSize != entry.ProxyFileSizeBytes)
            return null;

        _store.Touch(fingerprint, preset, DateTime.UtcNow);
        return new ProxyResolution(
            absolutePath,
            fingerprint,
            preset,
            entry.OriginalLogicalFrameSize,
            entry.ProxyDecodedFrameSize);
    }

    private string GetAbsolutePath(ProxyEntry entry)
    {
        string relative = entry.ProxyFileRelative.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(_store.StoreRootPath, relative));
    }

    private void OnStoreChanged(object? sender, ProxyStoreChangedEventArgs e)
    {
        if (e.Kind is ProxyStoreChangeKind.Registered
            or ProxyStoreChangeKind.StateChanged
            or ProxyStoreChangeKind.Deleted)
        {
            Interlocked.Increment(ref _version);
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
