using System.Collections.Concurrent;

namespace Beutl.Media.Proxy;

public sealed class ProxyResolver : IProxyResolver
{
    // Resolve is on the preview hot path, and the staleness scan enumerates the store, so bound it
    // to one scan per source path per interval.
    private static readonly TimeSpan s_stalenessCheckInterval = TimeSpan.FromSeconds(30);
    private const float DensityTolerance = 1e-6f;

    private readonly IProxyStore _store;
    private readonly ConcurrentDictionary<string, int> _pins = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _sourceVersions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _stalenessLastChecked = new(StringComparer.Ordinal);

    public ProxyResolver(IProxyStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _store.Changed += OnStoreChanged;
    }

    public long GetSourceVersion(ProxyFingerprint source)
    {
        string absolutePath = source.AbsolutePath;
        return !string.IsNullOrEmpty(absolutePath) && _sourceVersions.TryGetValue(absolutePath, out long version)
            ? version
            : 0;
    }

    public ProxyResolution? Resolve(Uri sourceUri, ProxyPreset preferredPreset)
    {
        ArgumentNullException.ThrowIfNull(sourceUri);
        if (!sourceUri.IsFile)
            return null;

        if (!ProxyFingerprint.TryFromFile(sourceUri.LocalPath, out ProxyFingerprint fingerprint))
            return null;

        // A mid-session source edit leaves a same-path, different-(size,mtime) entry Ready in the
        // store until the next startup reconcile; surface it as Stale now so the badge refreshes.
        MaybeMarkStaleEntries(fingerprint);

        // preferredPreset is a resolve-time density cap, not only a generation-time floor: prefer the
        // densest Ready proxy whose actual decoded density does not exceed the cap, so clamped large
        // sources are ranked by the proxy file that exists rather than by the preset's nominal scale.
        // Fall back to the densest Ready proxy overall if nothing fits — denser-than-requested is still
        // cheaper than the original.
        float cap = ScaleOf(preferredPreset);
        ProxyResolution? cappedWinner = null;
        float cappedWinnerDensity = -1f;
        ProxyResolution? densestWinner = null;
        float densestDensity = -1f;

        foreach (ProxyPreset preset in ProxyPresetDefinitions.All.Keys)
        {
            if (Evaluate(fingerprint, preset) is not { } resolution)
                continue;

            float density = resolution.SupplyDensity;
            if (density <= cap + DensityTolerance && density > cappedWinnerDensity)
            {
                cappedWinner = resolution;
                cappedWinnerDensity = density;
            }

            if (density > densestDensity)
            {
                densestWinner = resolution;
                densestDensity = density;
            }
        }

        ProxyResolution? chosen = cappedWinner ?? densestWinner;
        if (chosen is null)
            return null;

        // Touch only the proxy we actually hand out (contract: exactly once per successful Resolve).
        _store.Touch(chosen.Source, chosen.Preset, DateTime.UtcNow);
        return chosen;
    }

    private static float ScaleOf(ProxyPreset preset)
    {
        return ProxyPresetDefinitions.Get(preset).Scale;
    }

    /// <summary>
    /// Takes a transient, reference-counted decode-lifetime safety pin on a resolved proxy file so
    /// eviction cannot delete it while a MediaReader is decoding it. Dispose the returned handle to
    /// release the reference. This is NOT the future user-facing "do-not-evict" pin (FR-018a); that
    /// is a separate, persistent, user-driven concept — do not conflate the two.
    /// </summary>
    public IDisposable Pin(ProxyResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        string path = resolution.AbsoluteProxyFilePath;
        _pins.AddOrUpdate(path, 1, static (_, count) => checked(count + 1));
        return new PinHandle(this, path);
    }

    /// <summary>
    /// True while at least one transient decode-lifetime safety pin (see <see cref="Pin"/>) is held
    /// for the proxy file. Unrelated to FR-018a's future persistent user pin.
    /// </summary>
    public bool IsPinned(string absoluteProxyFilePath)
    {
        return _pins.TryGetValue(Path.GetFullPath(absoluteProxyFilePath), out int count) && count > 0;
    }

    private ProxyResolution? Evaluate(ProxyFingerprint fingerprint, ProxyPreset preset)
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

        return new ProxyResolution(
            absolutePath,
            fingerprint,
            preset,
            entry.OriginalLogicalFrameSize,
            entry.ProxyDecodedFrameSize);
    }

    private void MaybeMarkStaleEntries(ProxyFingerprint current)
    {
        string path = current.AbsolutePath;
        if (string.IsNullOrEmpty(path))
            return;

        DateTime nowUtc = DateTime.UtcNow;
        if (_stalenessLastChecked.TryGetValue(path, out DateTime last)
            && nowUtc - last < s_stalenessCheckInterval)
        {
            return;
        }

        _stalenessLastChecked[path] = nowUtc;

        // Filter the full enumeration by source path. The throttle above bounds how often this runs
        // per path, so a linear scan is acceptable here without a dedicated path index in the store.
        foreach (ProxyEntry entry in _store.Enumerate())
        {
            if (entry.State != ProxyState.Ready)
                continue;

            if (!string.Equals(entry.Source.AbsolutePath, path, StringComparison.Ordinal))
                continue;

            // Same path but different (size, mtime) ⇒ the source was edited/replaced after this proxy
            // was generated. Transition the old entry to Stale so the UI badge refreshes and the user
            // is prompted to regenerate. TryTransition is a no-op if another caller raced us to it.
            if (entry.Source != current)
                _store.TryTransition(entry.Source, entry.Preset, ProxyState.Stale);
        }
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
        // Decrement-and-remove-if-zero, atomic against concurrent Pin. The naive TryGetValue →
        // TryRemove is racy: a Pin landing between seeing count 0 and TryRemove would add a fresh
        // reference that TryRemove then drops. Loop on the observed value and only act while it still
        // matches: decrement via TryUpdate(old → old-1, old), or remove via the KeyValuePair overload
        // of TryRemove which removes only if the value still equals what we observed.
        while (_pins.TryGetValue(path, out int current))
        {
            if (current > 1)
            {
                if (_pins.TryUpdate(path, current - 1, current))
                    return;
            }
            else
            {
                if (_pins.TryRemove(new KeyValuePair<string, int>(path, current)))
                    return;
            }
        }

        // Key absent: a concurrent Unpin already cleaned it up. Idempotent.
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
