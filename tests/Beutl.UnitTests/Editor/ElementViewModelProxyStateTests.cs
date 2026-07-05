using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Media;
using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Editor;

// F-TL-2: the per-clip timeline proxy badge maps a clip's backing source to a single ProxyState.
// ElementViewModel.ResolveProxyState is the pure mapping that drives the badge color/tooltip.
[TestFixture]
public sealed class ElementViewModelProxyStateTests
{
    private static readonly string s_path = Path.Combine(Path.GetTempPath(), "proxy-badge-clip.mov");

    private static ProxyFingerprint Fingerprint(long size = 1000)
        => new(s_path, size, DateTime.UtcNow);

    private static ProxyEntry Entry(ProxyFingerprint source, ProxyState state, ProxyPreset preset = ProxyPreset.Quarter)
        => new(
            source,
            preset,
            state,
            "proxy/clip.mp4",
            1234,
            new PixelSize(1920, 1080),
            new PixelSize(480, 270),
            DateTime.UtcNow,
            DateTime.UtcNow,
            state == ProxyState.Failed ? "boom" : null);

    [Test]
    public void ResolveProxyState_NoEntryNoJob_IsNone()
    {
        ProxyFingerprint fp = Fingerprint();
        var store = new FakeProxyStore();

        Assert.That(ElementViewModel.ResolveProxyState(store, null, fp), Is.EqualTo(ProxyState.None));
    }

    [Test]
    public void ResolveProxyState_ReadyEntryForCurrentFingerprint_IsReady()
    {
        ProxyFingerprint fp = Fingerprint();
        var store = new FakeProxyStore(Entry(fp, ProxyState.Ready));

        Assert.That(ElementViewModel.ResolveProxyState(store, null, fp), Is.EqualTo(ProxyState.Ready));
    }

    [Test]
    public void ResolveProxyState_EntryForOutdatedFingerprint_IsStale()
    {
        ProxyFingerprint current = Fingerprint(size: 1000);
        ProxyFingerprint outdated = Fingerprint(size: 2000);
        var store = new FakeProxyStore(Entry(outdated, ProxyState.Ready));

        Assert.That(ElementViewModel.ResolveProxyState(store, null, current), Is.EqualTo(ProxyState.Stale));
    }

    [Test]
    public void ResolveProxyState_FailedEntry_IsFailed()
    {
        ProxyFingerprint fp = Fingerprint();
        var store = new FakeProxyStore(Entry(fp, ProxyState.Failed));

        Assert.That(ElementViewModel.ResolveProxyState(store, null, fp), Is.EqualTo(ProxyState.Failed));
    }

    [Test]
    public void ResolveProxyState_ReadyOutranksFailedAcrossPresets()
    {
        ProxyFingerprint fp = Fingerprint();
        var store = new FakeProxyStore(
            Entry(fp, ProxyState.Failed, ProxyPreset.Half),
            Entry(fp, ProxyState.Ready, ProxyPreset.Quarter));

        Assert.That(ElementViewModel.ResolveProxyState(store, null, fp), Is.EqualTo(ProxyState.Ready));
    }

    [Test]
    public void ResolveProxyState_PendingJob_IsGenerating()
    {
        ProxyFingerprint fp = Fingerprint();
        var store = new FakeProxyStore();
        var queue = new FakeProxyJobQueue(new ProxyJob(fp, ProxyPreset.Quarter));

        Assert.That(ElementViewModel.ResolveProxyState(store, queue, fp), Is.EqualTo(ProxyState.Generating));
    }

    // Fix 1: the badge caches fingerprints keyed on the ordered set of source URIs. A high-frequency
    // store/queue refresh (invalidateCache: false) must not re-stat, so an in-place overwrite (same
    // URIs) is not observed until a ThumbnailsInvalidated refresh busts the cache (invalidateCache: true).
    [Test]
    public void ResolveCachedFingerprints_SameUrisWithoutInvalidate_ReturnsCachedWithoutRestat()
    {
        var uris = new[] { new Uri(s_path) };
        ProxyFingerprint before = Fingerprint(size: 1000);
        ProxyFingerprint after = Fingerprint(size: 2000);
        ProxyFingerprint current = before;
        int statCalls = 0;
        Func<Uri, ProxyFingerprint?> stat = _ =>
        {
            statCalls++;
            return current;
        };

        string? cachedKey = null;
        IReadOnlyList<ProxyFingerprint> cached = [];

        ElementViewModel.ResolveCachedFingerprints(uris, false, ref cachedKey, ref cached, stat);
        current = after;
        IReadOnlyList<ProxyFingerprint> resolved =
            ElementViewModel.ResolveCachedFingerprints(uris, false, ref cachedKey, ref cached, stat);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.EqualTo(new[] { before }));
            Assert.That(statCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void ResolveCachedFingerprints_InvalidateCache_ReStatsSameUris()
    {
        var uris = new[] { new Uri(s_path) };
        ProxyFingerprint before = Fingerprint(size: 1000);
        ProxyFingerprint after = Fingerprint(size: 2000);
        ProxyFingerprint current = before;
        Func<Uri, ProxyFingerprint?> stat = _ => current;

        string? cachedKey = null;
        IReadOnlyList<ProxyFingerprint> cached = [];

        ElementViewModel.ResolveCachedFingerprints(uris, false, ref cachedKey, ref cached, stat);
        current = after;
        IReadOnlyList<ProxyFingerprint> resolved =
            ElementViewModel.ResolveCachedFingerprints(uris, true, ref cachedKey, ref cached, stat);

        Assert.That(resolved, Is.EqualTo(new[] { after }));
    }

    [Test]
    public void ResolveCachedFingerprints_ChangedUriSet_ReStatsWithoutInvalidate()
    {
        string otherPath = Path.Combine(Path.GetTempPath(), "proxy-badge-clip-2.mov");
        ProxyFingerprint first = Fingerprint(size: 1000);
        Func<Uri, ProxyFingerprint?> stat = u =>
            u.LocalPath == s_path ? first : new ProxyFingerprint(otherPath, 500, DateTime.UtcNow);

        string? cachedKey = null;
        IReadOnlyList<ProxyFingerprint> cached = [];

        ElementViewModel.ResolveCachedFingerprints([new Uri(s_path)], false, ref cachedKey, ref cached, stat);
        IReadOnlyList<ProxyFingerprint> resolved = ElementViewModel.ResolveCachedFingerprints(
            [new Uri(s_path), new Uri(otherPath)], false, ref cachedKey, ref cached, stat);

        Assert.That(resolved, Has.Count.EqualTo(2));
    }

    [Test]
    public void InPlaceOverwrite_CacheBustFlipsResolvedStateFromStaleToReady()
    {
        // A fresh Ready proxy exists for the overwritten file (new fingerprint). Before the cache-bust
        // the badge compares the store entry against the stale fingerprint and reads Stale; after the
        // bust the fingerprint matches the entry and reads Ready.
        var uris = new[] { new Uri(s_path) };
        ProxyFingerprint before = Fingerprint(size: 1000);
        ProxyFingerprint after = Fingerprint(size: 2000);
        ProxyFingerprint current = before;
        Func<Uri, ProxyFingerprint?> stat = _ => current;
        var store = new FakeProxyStore(Entry(after, ProxyState.Ready));

        string? cachedKey = null;
        IReadOnlyList<ProxyFingerprint> cached = [];

        ElementViewModel.ResolveCachedFingerprints(uris, false, ref cachedKey, ref cached, stat);
        current = after;

        ProxyFingerprint stale =
            ElementViewModel.ResolveCachedFingerprints(uris, false, ref cachedKey, ref cached, stat)[0];
        ProxyFingerprint fresh =
            ElementViewModel.ResolveCachedFingerprints(uris, true, ref cachedKey, ref cached, stat)[0];

        Assert.Multiple(() =>
        {
            Assert.That(ElementViewModel.ResolveProxyState(store, null, stale), Is.EqualTo(ProxyState.Stale));
            Assert.That(ElementViewModel.ResolveProxyState(store, null, fresh), Is.EqualTo(ProxyState.Ready));
        });
    }

    // Fix 3: an element backing onto several sources must aggregate every source's state, not read
    // one FirstOrDefault. Active generation dominates; otherwise a readiness disagreement is Partial.
    [Test]
    public void AggregateProxyState_AllReady_IsReady()
    {
        ProxyFingerprint a = Fingerprint(size: 1000);
        ProxyFingerprint b = new(Path.Combine(Path.GetTempPath(), "clip-b.mov"), 2000, DateTime.UtcNow);
        var store = new FakeProxyStore(Entry(a, ProxyState.Ready), Entry(b, ProxyState.Ready));

        Assert.That(ElementViewModel.AggregateProxyState(store, null, [a, b]), Is.EqualTo(ProxyState.Ready));
    }

    [Test]
    public void AggregateProxyState_ReadyAndMissing_IsPartial()
    {
        ProxyFingerprint a = Fingerprint(size: 1000);
        ProxyFingerprint b = new(Path.Combine(Path.GetTempPath(), "clip-b.mov"), 2000, DateTime.UtcNow);
        var store = new FakeProxyStore(Entry(a, ProxyState.Ready));

        Assert.That(ElementViewModel.AggregateProxyState(store, null, [a, b]), Is.EqualTo(ProxyState.Partial));
    }

    [Test]
    public void AggregateProxyState_AnyGenerating_IsGenerating()
    {
        ProxyFingerprint a = Fingerprint(size: 1000);
        ProxyFingerprint b = new(Path.Combine(Path.GetTempPath(), "clip-b.mov"), 2000, DateTime.UtcNow);
        var store = new FakeProxyStore(Entry(a, ProxyState.Ready));
        var queue = new FakeProxyJobQueue(new ProxyJob(b, ProxyPreset.Quarter));

        Assert.That(ElementViewModel.AggregateProxyState(store, queue, [a, b]), Is.EqualTo(ProxyState.Generating));
    }

    [Test]
    public void AggregateProxyState_NoFingerprints_IsNone()
    {
        var store = new FakeProxyStore();

        Assert.That(ElementViewModel.AggregateProxyState(store, null, []), Is.EqualTo(ProxyState.None));
    }

    private sealed class FakeProxyStore(params ProxyEntry[] entries) : IProxyStore
    {
        public string StoreRootPath => Path.GetTempPath();

        public ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset)
            => entries.FirstOrDefault(e => e.Source == source && e.Preset == preset);

        public IReadOnlyList<ProxyEntry> Enumerate() => entries;

        public void Register(ProxyEntry entry) => throw new NotSupportedException();

        public bool TryTransition(ProxyFingerprint source, ProxyPreset preset, ProxyState newState, string? failureReason = null)
            => throw new NotSupportedException();

        public bool Delete(ProxyFingerprint source, ProxyPreset preset) => throw new NotSupportedException();

        public void Touch(ProxyFingerprint source, ProxyPreset preset, DateTime nowUtc)
        {
        }

        public long GetTotalBytes() => 0;

        public long GetTotalBytes(IReadOnlySet<string> sourceAbsolutePaths) => 0;

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReconcileAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public event EventHandler<ProxyStoreChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class FakeProxyJobQueue(params ProxyJob[] pending) : IProxyJobQueue
    {
        public int MaxConcurrency => 1;

        public ValueTask<ProxyJob> EnqueueAsync(ProxyFingerprint source, ProxyPreset preset, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IReadOnlyList<ProxyJob> Pending() => pending;

        public void Cancel(Guid jobId)
        {
        }

        public void CancelAll()
        {
        }

        public event EventHandler<ProxyJobChangedEventArgs>? JobChanged
        {
            add { }
            remove { }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
