using System.Reactive;
using Avalonia.Media;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class ElementViewModel
{
    private void InitializeProxyIndicator()
    {
        if (_proxyStore != null)
        {
            EventHandler<ProxyStoreChangedEventArgs> storeHandler = (_, e) =>
            {
                if (AffectsProxyBadge(e.Kind))
                    OnProxyStateInvalidated(e);

                OnProxyStoreChangedForThumbnails(e);
            };
            _proxyStore.Changed += storeHandler;
            Disposable.Create(() => _proxyStore.Changed -= storeHandler).AddTo(_disposables);
        }

        if (_proxyJobQueue != null)
        {
            EventHandler<ProxyJobChangedEventArgs> jobHandler = (_, e) =>
            {
                if (AffectsProxyIndicator(e.Kind))
                    OnProxyStateInvalidated(e);
            };
            _proxyJobQueue.JobChanged += jobHandler;
            Disposable.Create(() => _proxyJobQueue.JobChanged -= jobHandler).AddTo(_disposables);
        }

        // Source edits raise ThumbnailsInvalidated; re-resolve the badge when the backing file changes.
        // An in-place overwrite keeps the same URI, so the fingerprint cache must be busted to re-stat.
        _thumbnailsInvalidatedSubject
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOnUIDispatcher()
            .Subscribe(_ => RefreshProxyState(invalidateFingerprintCache: true))
            .AddTo(_disposables);

        // Sources behind a node graph or a referenced scene raise no ThumbnailsInvalidated, so Element.Edited
        // re-resolves the badge too. Dispose disposes the subject before this handler is removed.
        EventHandler editedHandler = (_, _) =>
        {
            if (!_isDisposed)
                _elementEditedSubject.OnNext(Unit.Default);
        };
        Model.Edited += editedHandler;
        Disposable.Create(() => Model.Edited -= editedHandler).AddTo(_disposables);
        _elementEditedSubject
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOnUIDispatcher()
            .Subscribe(_ => RefreshProxyState())
            .AddTo(_disposables);

        RefreshProxyState(invalidateFingerprintCache: true);
    }

    private void OnProxyStateInvalidated(ProxyJobChangedEventArgs e)
    {
        OnUIThreadIfRelevant(e.Job.Source.AbsolutePath, () => RefreshProxyState(invalidateFingerprintCache: true));
    }

    // Runs action on the UI thread unless the element is disposed or does not use the changed source. A null
    // sourceKey, from a store Reset, skips that check.
    private void OnUIThreadIfRelevant(string? sourceKey, Action action)
    {
        if (_isDisposed)
            return;

        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnUIThreadIfRelevant(sourceKey, action));
            return;
        }

        if (sourceKey != null && !ElementUsesChangedSourceCached(sourceKey))
            return;

        action();
    }

    private static string? GetChangedSourceKey(ProxyStoreChangedEventArgs e)
        => e.Kind == ProxyStoreChangeKind.Reset ? null : e.Source.AbsolutePath;

    private IReadOnlyList<ProxyFingerprint> ResolveProxyFingerprints(bool invalidateCache)
    {
        // Cover every proxy-aware holder (VideoSourceNode graph inputs, referenced scenes, animated
        // values) so an element that uses video through any of those paths contributes to the badge,
        // not just a top-level SourceVideo's current value. Dedup file URIs in a stable order.
        List<Uri> uris = [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (VideoSource source in ProxySourceEnumerator.EnumerateVideoSources(Model))
        {
            if (source is { HasUri: true }
                && source.Uri is { IsFile: true } uri
                && seen.Add(uri.LocalPath))
            {
                uris.Add(uri);
            }
        }

        return ResolveCachedFingerprints(
            uris,
            invalidateCache,
            ref _proxySourceKey,
            ref _proxyFingerprints,
            StatOrProxyFallback);
    }

    private ProxyFingerprint? StatOrProxyFallback(Uri uri)
        => ResolveSourceFingerprint(_proxyStore, uri);

    // A Ready proxy still opens when the original is gone, so when TryFromFile fails this uses the fingerprint
    // of the newest same-path entry, or the path key when there is none.
    internal static ProxyFingerprint? ResolveSourceFingerprint(IProxyStore? store, Uri uri)
    {
        if (ProxyFingerprint.TryFromFile(uri.LocalPath, out ProxyFingerprint fingerprint))
            return fingerprint;

        string key = ProxyFingerprint.ResolveComparableKey(uri.LocalPath);
        if (store is not null)
        {
            // Only the newest same-path source counts, so an older Ready proxy does not show as Ready. Its exact
            // fingerprint is returned, since a path key would read as Stale and hide a failed generation.
            List<ProxyEntry> pathEntries = [.. store.Enumerate()
                .Where(e => string.Equals(e.Source.AbsolutePath, key, StringComparison.Ordinal))];
            if (pathEntries.Count > 0)
            {
                Dictionary<ProxyFingerprint, DateTime> newestBySource = pathEntries
                    .GroupBy(e => e.Source)
                    .ToDictionary(g => g.Key, g => g.Max(e => e.GeneratedAtUtc));
                ProxyFingerprint newest = pathEntries
                    .OrderByDescending(e => newestBySource[e.Source])
                    .ThenByDescending(e => e.Source.MtimeUtc)
                    .First().Source;
                return newest;
            }
        }

        return ProxyFingerprint.ForPathKey(uri.LocalPath);
    }

    // Keyed on the ordered set of source URIs so high-frequency store/queue refreshes never re-stat
    // the files; an in-place overwrite keeps the same URIs, so those callers pass invalidateCache to
    // force one re-stat.
    internal static IReadOnlyList<ProxyFingerprint> ResolveCachedFingerprints(
        IReadOnlyList<Uri> currentUris,
        bool invalidateCache,
        ref string? cachedKey,
        ref IReadOnlyList<ProxyFingerprint> cachedFingerprints,
        Func<Uri, ProxyFingerprint?> stat)
    {
        string key = string.Join("\n", currentUris.Select(static u => u.LocalPath));
        if (!invalidateCache && string.Equals(key, cachedKey, StringComparison.Ordinal))
            return cachedFingerprints;

        var fingerprints = new List<ProxyFingerprint>(currentUris.Count);
        foreach (Uri uri in currentUris)
        {
            if (stat(uri) is { } fingerprint)
                fingerprints.Add(fingerprint);
        }

        cachedKey = key;
        cachedFingerprints = fingerprints;
        return fingerprints;
    }

    private void OnProxyStateInvalidated(ProxyStoreChangedEventArgs e)
    {
        // A registration, a state change or a deletion can change what ResolveSourceFingerprint picks, so re-stat.
        OnUIThreadIfRelevant(GetChangedSourceKey(e), () => RefreshProxyState(invalidateFingerprintCache: true));
    }

    // Matches the cached fingerprints, so a burst of store and job events does not re-stat the source files on
    // the UI thread.
    private bool ElementUsesChangedSourceCached(string changedSourceKey)
    {
        foreach (ProxyFingerprint fingerprint in ResolveProxyFingerprints(invalidateCache: false))
        {
            if (string.Equals(fingerprint.AbsolutePath, changedSourceKey, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private void RefreshProxyState(bool invalidateFingerprintCache = false)
    {
        IReadOnlyList<ProxyFingerprint> fingerprints = ResolveProxyFingerprints(invalidateFingerprintCache);
        if (_proxyStore is not { } store || fingerprints.Count == 0)
        {
            ShowProxyIndicator.Value = false;
            ProxyIndicatorState.Value = ProxyState.None;
            return;
        }

        ProxyIndicatorState.Value = AggregateProxyState(store, _proxyJobQueue, fingerprints);
        ShowProxyIndicator.Value = true;
    }

    internal static ProxyState AggregateProxyState(
        IProxyStore store, IProxyJobQueue? queue, IReadOnlyList<ProxyFingerprint> fingerprints)
    {
        ArgumentNullException.ThrowIfNull(store);

        // Snapshot the store/queue once for the whole element instead of per fingerprint: each
        // Enumerate()/Pending() allocates, and a store/job event fans out to every element on the
        // UI thread, so the per-fingerprint scans compounded across a large project.
        IReadOnlyList<ProxyEntry> entries = store.Enumerate();
        IReadOnlyList<ProxyJob> pending = queue?.Pending() ?? [];

        ProxyState? aggregate = null;
        foreach (ProxyFingerprint fingerprint in fingerprints)
        {
            ProxyState state = ResolveProxyState(entries, pending, fingerprint);
            aggregate = aggregate is { } current ? CombineProxyStates(current, state) : state;
        }

        return aggregate ?? ProxyState.None;
    }

    // A clip can back onto several sources (a graph with multiple inputs, a referenced scene). Active
    // generation dominates so the badge reflects in-flight work; otherwise any disagreement in
    // readiness collapses to Partial rather than a single source's state hiding the others.
    private static ProxyState CombineProxyStates(ProxyState a, ProxyState b)
    {
        if (a == b)
            return a;
        if (a == ProxyState.Generating || b == ProxyState.Generating)
            return ProxyState.Generating;

        return ProxyState.Partial;
    }

    private static ProxyState ResolveProxyState(
        IReadOnlyList<ProxyEntry> entries, IReadOnlyList<ProxyJob> pending, ProxyFingerprint fingerprint)
    {
        foreach (ProxyJob job in pending)
        {
            if (string.Equals(job.Source.AbsolutePath, fingerprint.AbsolutePath, StringComparison.Ordinal))
                return ProxyState.Generating;
        }

        ProxyState? best = null;
        bool bestIsExact = false;
        foreach (ProxyEntry entry in entries)
        {
            if (!string.Equals(entry.Source.AbsolutePath, fingerprint.AbsolutePath, StringComparison.Ordinal))
                continue;

            bool isExact = entry.Source == fingerprint;
            ProxyState effective = isExact ? entry.State : ProxyState.Stale;

            if (best is not { } current)
            {
                best = effective;
                bestIsExact = isExact;
                continue;
            }

            // An exact entry is the current source and beats an old one, which would otherwise hide a Failed
            // state as Stale. Among equally exact entries, the higher rank wins.
            bool takeNew = isExact != bestIsExact
                ? isExact
                : ProxyStateRank(effective) > ProxyStateRank(current);
            if (takeNew)
            {
                best = effective;
                bestIsExact = isExact;
            }
        }

        return best ?? ProxyState.None;
    }

    // Progressed is a fractional update; it does not change the badge's queued/running/ready/failed state.
    internal static bool AffectsProxyIndicator(ProxyJobChangeKind kind) => kind != ProxyJobChangeKind.Progressed;

    // Touched is an LRU bump on reader-open, not a state change; excluding it avoids a badge re-walk
    // per clip per reader-open during bulk generate.
    internal static bool AffectsProxyBadge(ProxyStoreChangeKind kind)
        => kind is ProxyStoreChangeKind.Registered
            or ProxyStoreChangeKind.StateChanged
            or ProxyStoreChangeKind.Deleted
            or ProxyStoreChangeKind.Reset;

    private static int ProxyStateRank(ProxyState state) => state switch
    {
        ProxyState.Ready => 5,
        ProxyState.Generating => 4,
        ProxyState.Stale => 3,
        ProxyState.Partial => 2,
        ProxyState.Failed => 1,
        _ => 0,
    };

    private static IBrush GetProxyStateBrush(ProxyState state) => state switch
    {
        ProxyState.Ready => s_proxyReadyBrush,
        ProxyState.Generating => s_proxyGeneratingBrush,
        ProxyState.Stale or ProxyState.Partial => s_proxyStaleBrush,
        ProxyState.Failed => s_proxyFailedBrush,
        _ => s_proxyNoneBrush,
    };

    private static string GetProxyStateText(ProxyState state) => state switch
    {
        ProxyState.Ready => Strings.ProxyReady,
        ProxyState.Generating => Strings.ProxyGenerating,
        ProxyState.Stale => Strings.ProxyStale,
        ProxyState.Partial => Strings.ProxyPartial,
        ProxyState.Failed => Strings.ProxyFailed,
        _ => Strings.ProxyMissing,
    };
}
