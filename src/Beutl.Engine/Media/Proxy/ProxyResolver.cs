using System.Collections.Concurrent;

namespace Beutl.Media.Proxy;

public sealed class ProxyResolver : IProxyResolver
{
    // Resolve is on the preview hot path, and the staleness scan enumerates the store, so bound it
    // to one scan per source path per interval.
    private static readonly TimeSpan s_stalenessCheckInterval = TimeSpan.FromSeconds(30);
    private const float DensityTolerance = 1e-6f;

    private readonly IProxyStore _store;
    private readonly long _versionBaseOffset;
    private readonly ConcurrentDictionary<string, int> _pins = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _sourceVersions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (DateTime Time, ProxyFingerprint Fingerprint)> _stalenessLastChecked = new(StringComparer.Ordinal);

    public ProxyResolver(IProxyStore store)
        : this(store, versionBaseOffset: 0)
    {
    }

    // versionBaseOffset is added to every reported source version. On a store-root swap the replacement
    // resolver takes a strictly larger offset than the old one issued, so a reader that cached the old
    // resolver's version (commonly 0 for a startup-index entry) observes a change and reopens against the
    // new store instead of decoding the previous directory's proxy under the same version.
    public ProxyResolver(IProxyStore store, long versionBaseOffset)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfNegative(versionBaseOffset);
        _store = store;
        _versionBaseOffset = versionBaseOffset;
        _store.Changed += OnStoreChanged;
    }

    public long GetSourceVersion(string absolutePath)
    {
        long version = !string.IsNullOrEmpty(absolutePath) && _sourceVersions.TryGetValue(absolutePath, out long v)
            ? v
            : 0;
        return _versionBaseOffset + version;
    }

    public ProxyResolution? Resolve(Uri sourceUri, ProxyPreset preferredPreset)
    {
        ArgumentNullException.ThrowIfNull(sourceUri);
        if (!sourceUri.IsFile)
            return null;

        if (!ProxyFingerprint.TryFromFile(sourceUri.LocalPath, out ProxyFingerprint fingerprint))
        {
            // Offline originals can use a Ready proxy indexed by the same normalized path.
            return ResolveByPath(ProxyFingerprint.ResolveComparableKey(sourceUri.LocalPath), preferredPreset);
        }

        // A mid-session source edit leaves a same-path, different-(size,mtime) entry Ready in the
        // store until the next startup reconcile; surface it as Stale now so the badge refreshes.
        MaybeMarkStaleEntries(fingerprint);

        var candidates = new List<ProxyResolution>();
        foreach (ProxyPreset preset in ProxyPresetDefinitions.All.Keys)
        {
            if (Evaluate(fingerprint, preset) is { } resolution)
                candidates.Add(resolution);
        }

        return SelectBest(candidates, preferredPreset);
    }

    private ProxyResolution? ResolveByPath(string absolutePath, ProxyPreset preferredPreset)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return null;

        // With no current fingerprint, pick the newest source version from same-path entries in every state, so a
        // failed regeneration for a replaced original wins over an older Ready proxy and yields no proxy.
        var matches = new List<(ProxyEntry Entry, ProxyResolution? Resolution)>();
        foreach (ProxyEntry entry in _store.Enumerate())
        {
            if (!string.Equals(entry.Source.AbsolutePath, absolutePath, StringComparison.Ordinal))
                continue;

            ProxyResolution? resolution = entry.State == ProxyState.Ready ? EvaluateEntry(entry) : null;
            matches.Add((entry, resolution));
        }

        if (matches.Count == 0)
            return null;

        ProxyFingerprint newestSource = NewestSource(matches.Select(m => m.Entry));
        var candidates = matches
            .Where(m => m.Entry.Source == newestSource && m.Resolution is not null)
            .Select(m => m.Resolution!)
            .ToList();

        return SelectBest(candidates, preferredPreset);
    }

    // Ranks sources by their newest generation (precomputed once, not per comparison), then by source mtime.
    internal static ProxyFingerprint NewestSource(IEnumerable<ProxyEntry> entries)
    {
        Dictionary<ProxyFingerprint, DateTime> newestBySource = entries
            .GroupBy(e => e.Source)
            .ToDictionary(g => g.Key, g => g.Max(e => e.GeneratedAtUtc));
        return entries
            .OrderByDescending(e => newestBySource[e.Source])
            .ThenByDescending(e => e.Source.MtimeUtc)
            .First().Source;
    }

    private ProxyResolution? SelectBest(IReadOnlyList<ProxyResolution> candidates, ProxyPreset preferredPreset)
    {
        ProxyResolution? chosen = SelectDensest(candidates, preferredPreset, resolution => resolution.SupplyDensity);
        if (chosen is null)
            return null;

        // Touch only the proxy we actually hand out (contract: exactly once per successful Resolve).
        _store.Touch(chosen.Source, chosen.Preset, DateTime.UtcNow);
        return chosen;
    }

    // preferredPreset caps the actual decoded density: prefer the densest proxy within it, else the densest overall,
    // which is still cheaper than the original.
    internal static T? SelectDensest<T>(IEnumerable<T> candidates, ProxyPreset preferredPreset, Func<T, float> supplyDensity)
        where T : class
    {
        float cap = ScaleOf(preferredPreset);
        T? cappedWinner = null;
        float cappedWinnerDensity = -1f;
        T? densestWinner = null;
        float densestDensity = -1f;

        foreach (T candidate in candidates)
        {
            float density = supplyDensity(candidate);
            if (density <= cap + DensityTolerance && density > cappedWinnerDensity)
            {
                cappedWinner = candidate;
                cappedWinnerDensity = density;
            }

            if (density > densestDensity)
            {
                densestWinner = candidate;
                densestDensity = density;
            }
        }

        return cappedWinner ?? densestWinner;
    }

    private static float ScaleOf(ProxyPreset preset)
    {
        return ProxyPresetDefinitions.Get(preset).Scale;
    }

    /// <summary>
    /// Takes a transient, reference-counted decode-lifetime safety pin on a resolved proxy file so
    /// eviction cannot delete it while a MediaReader is decoding it. Dispose the returned handle to
    /// release the reference.
    /// </summary>
    public IDisposable Pin(ProxyResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        if (_store is ProxyStore store)
            return store.Pin(resolution);

        string path = resolution.AbsoluteProxyFilePath;
        _pins.AddOrUpdate(path, 1, static (_, count) => checked(count + 1));
        return new PinHandle(this, path);
    }

    /// <summary>
    /// True while at least one transient decode-lifetime safety pin (see <see cref="Pin"/>) is held
    /// for the proxy file.
    /// </summary>
    public bool IsPinned(string absoluteProxyFilePath)
    {
        if (_store is ProxyStore store)
            return store.IsPinned(absoluteProxyFilePath);

        return _pins.TryGetValue(Path.GetFullPath(absoluteProxyFilePath), out int count) && count > 0;
    }

    private ProxyResolution? Evaluate(ProxyFingerprint fingerprint, ProxyPreset preset)
    {
        ProxyEntry? entry = _store.TryGet(fingerprint, preset);
        if (entry is not { State: ProxyState.Ready })
            return null;

        return EvaluateEntry(entry);
    }

    private ProxyResolution? EvaluateEntry(ProxyEntry entry)
    {
        if (!TryGetAbsolutePath(entry, out string absolutePath)
            || !ProxyEntryChecks.IsValidReadyFile(entry, absolutePath))
        {
            return null;
        }

        return new ProxyResolution(
            absolutePath,
            entry.Source,
            entry.Preset,
            entry.OriginalLogicalFrameSize,
            entry.ProxyDecodedFrameSize);
    }

    private void MaybeMarkStaleEntries(ProxyFingerprint current)
    {
        string path = current.AbsolutePath;
        if (string.IsNullOrEmpty(path))
            return;

        DateTime nowUtc = DateTime.UtcNow;
        // Throttle repeat scans of the same path, but only while the source fingerprint is unchanged: a
        // source replaced within the interval yields a new fingerprint, and skipping the scan would leave
        // its now-outdated Ready proxy marked Ready until the interval elapses (and never, if the source
        // then goes offline), letting offline ResolveByPath serve stale content.
        if (_stalenessLastChecked.TryGetValue(path, out (DateTime Time, ProxyFingerprint Fingerprint) last)
            && nowUtc - last.Time < s_stalenessCheckInterval
            && last.Fingerprint == current)
        {
            return;
        }

        _stalenessLastChecked[path] = (nowUtc, current);

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
        // Decrement or remove only while the count still equals the observed value, so a concurrent Pin is never dropped.
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
