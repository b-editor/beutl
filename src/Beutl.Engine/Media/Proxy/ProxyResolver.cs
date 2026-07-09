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
    private readonly ConcurrentDictionary<string, (DateTime Time, ProxyFingerprint Fingerprint)> _stalenessLastChecked = new(StringComparer.Ordinal);

    public ProxyResolver(IProxyStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _store.Changed += OnStoreChanged;
    }

    public long GetSourceVersion(string absolutePath)
    {
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
        {
            // The original was moved/deleted, so it cannot be fingerprinted. A Ready proxy for this
            // path is still a valid preview stand-in, so resolve by path alone rather than rejecting.
            // ResolveComparableKey folds/link-resolves the path the same way entries key their source.
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

        // The original is gone, so there is no current fingerprint to match. Determine the newest source
        // version from ALL same-path entries regardless of state, then serve a Ready proxy only from that
        // source: after an original is replaced, a failed regeneration records a newer Failed/Stale entry
        // for the new fingerprint while an older Ready proxy for the same path lingers. Ranking only Ready
        // entries would miss that newer source and preview the stale older proxy; including every state in
        // the version choice makes an unproxied-but-newer source win, yielding no proxy (fall to original).
        var matches = new List<(ProxyFingerprint Source, DateTime GeneratedAtUtc, ProxyResolution? Resolution)>();
        foreach (ProxyEntry entry in _store.Enumerate())
        {
            if (!string.Equals(entry.Source.AbsolutePath, absolutePath, StringComparison.Ordinal))
                continue;

            ProxyResolution? resolution = entry.State == ProxyState.Ready ? EvaluateEntry(entry) : null;
            matches.Add((entry.Source, entry.GeneratedAtUtc, resolution));
        }

        if (matches.Count == 0)
            return null;

        // Precompute newest generation per source once rather than re-scanning inside the sort comparer
        // (O(n²) on a path with many accumulated versions/presets). Rank by newest generation, then newer
        // source mtime as a stable tiebreak (a replaced original has a newer mtime even if two proxies
        // happen to share a generation timestamp).
        Dictionary<ProxyFingerprint, DateTime> newestBySource = matches
            .GroupBy(m => m.Source)
            .ToDictionary(g => g.Key, g => g.Max(m => m.GeneratedAtUtc));
        ProxyFingerprint newestSource = matches
            .OrderByDescending(m => newestBySource[m.Source])
            .ThenByDescending(m => m.Source.MtimeUtc)
            .First().Source;

        var candidates = matches
            .Where(m => m.Source == newestSource && m.Resolution is not null)
            .Select(m => m.Resolution!)
            .ToList();

        return SelectBest(candidates, preferredPreset);
    }

    private ProxyResolution? SelectBest(IReadOnlyList<ProxyResolution> candidates, ProxyPreset preferredPreset)
    {
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

        foreach (ProxyResolution resolution in candidates)
        {
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

        return EvaluateEntry(entry);
    }

    private ProxyResolution? EvaluateEntry(ProxyEntry entry)
    {
        if (entry.State != ProxyState.Ready)
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
