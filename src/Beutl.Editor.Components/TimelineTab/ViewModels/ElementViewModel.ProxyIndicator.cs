using System.Collections.Immutable;
using System.Reactive;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.Services;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Utilities;
using FluentAvalonia.UI.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
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

        // A source reachable only through a node graph or referenced scene never raises the top-level
        // provider's ThumbnailsInvalidated, so its URI edits would leave the fingerprint cache stale and
        // the badge frozen. Element.Edited fires for those edits too; the URI-set key in
        // ResolveProxyFingerprints keeps unrelated edits from re-stating.
        // Dispose() disposes _elementEditedSubject before _disposables removes this handler, so guard
        // against a teardown-time Edited raising OnNext on the disposed subject.
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
        if (_isDisposed)
            return;

        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnProxyStateInvalidated(e));
            return;
        }

        if (!ElementUsesChangedSourceCached(e.Job.Source.AbsolutePath))
            return;

        RefreshProxyState(invalidateFingerprintCache: true);
    }

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

    // Prefer-proxy preview can open a Ready proxy after the original is moved/deleted. TryFromFile then
    // fails, and dropping that URI would hide the badge and filter out the proxy's own store/job events.
    // Prefer the resolved Ready entry's own fingerprint (exact, so the badge reads Ready); otherwise fall
    // back to the path key so the offline source is still tracked before any proxy exists — a proxy
    // registered for this path then matches the store event and refreshes the badge/filmstrip, instead
    // of the URI staying absent from the cache until the view is rebuilt.
    internal static ProxyFingerprint? ResolveSourceFingerprint(IProxyStore? store, Uri uri)
    {
        if (ProxyFingerprint.TryFromFile(uri.LocalPath, out ProxyFingerprint fingerprint))
            return fingerprint;

        string key = ProxyFingerprint.ResolveComparableKey(uri.LocalPath);
        if (store is not null)
        {
            // Choose the newest same-path source across all states, then return a Ready fingerprint only
            // from that source — matching ProxyResolver.ResolveByPath, so the badge does not report Ready
            // off a stale older proxy when a newer regeneration failed for the current source.
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
                // Return the newest same-path source's own (exact) fingerprint even when it is not Ready.
                // A path-key fallback has no size/mtime, so ResolveProxyState treats every entry as
                // non-exact and forces Stale, hiding a failed generation for an offline clip; the exact
                // newest fingerprint lets the badge read the real state while still never reporting Ready
                // off a stale older proxy (newest, not any-Ready, is chosen).
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

    private void OnProxyStateInvalidated()
    {
        if (_isDisposed)
            return;

        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(OnProxyStateInvalidated);
            return;
        }

        RefreshProxyState();
    }

    private void OnProxyStateInvalidated(ProxyStoreChangedEventArgs e)
    {
        if (_isDisposed)
            return;

        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnProxyStateInvalidated(e));
            return;
        }

        // A Reset (store swap) has no meaningful Source, so skip the per-source relevance gate and always
        // re-resolve; otherwise ElementUsesChangedSourceCached reads Model (UI-thread state) here so a
        // store event for an unrelated source skips the per-clip walk in RefreshProxyState.
        if (e.Kind != ProxyStoreChangeKind.Reset && !ElementUsesChangedSourceCached(e.Source.AbsolutePath))
            return;

        // A store registration/state change/delete can flip which entry ResolveSourceFingerprint
        // picks — in particular it turns an offline ForPathKey fingerprint (cached before any Ready
        // proxy existed) into the Ready entry's exact fingerprint, or back — so re-resolve the cache;
        // otherwise the row stays Stale until an unrelated edit invalidates it. Store events are
        // per-source (already relevance-gated), not the high-frequency queue refresh, so re-stat here.
        RefreshProxyState(invalidateFingerprintCache: true);
    }

    internal static bool ElementUsesChangedSource(Element element, string changedSourceKey)
    {
        // Match against every proxy-aware source (not just the first), so a store event for a source
        // reached only through an animated value / graph input / referenced scene still invalidates
        // the filmstrip. Resolve via FromFile so a symlinked source matches its own event key.
        foreach (VideoSource source in ProxySourceEnumerator.EnumerateVideoSources(element))
        {
            if (source is { HasUri: true }
                && source.Uri is { IsFile: true } uri
                && ProxyFingerprint.TryFromFile(uri.LocalPath, out ProxyFingerprint fingerprint)
                && string.Equals(fingerprint.AbsolutePath, changedSourceKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Per-event relevance gate: matches against this element's cached fingerprints so a burst of
    // store/job events never re-stats the source files (ResolveLinkTarget) on the UI thread. The
    // cache re-stats only when the source URI set changes or a source edit fires ThumbnailsInvalidated.
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

    internal static ProxyState ResolveProxyState(IProxyStore store, IProxyJobQueue? queue, ProxyFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(store);
        return ResolveProxyState(store.Enumerate(), queue?.Pending() ?? [], fingerprint);
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

            // An exact-fingerprint entry reflects the current source, so it always wins over an old
            // (forced-Stale) entry — otherwise a same-path Stale leftover would outrank the current
            // fingerprint's Failed and hide the real state. Among equally-exact entries, rank wins.
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
