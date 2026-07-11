using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reactive.Disposables;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.ProxiesTab.ViewModels;

public sealed class ProxiesTabViewModel : IDisposable, IToolContext
{
    private static readonly ProxyPreset[] s_presetOrder =
    [
        ProxyPreset.Half,
        ProxyPreset.Quarter,
        ProxyPreset.Eighth,
    ];

    // Heaviness floor for the bulk "generate all" action (SC-001 targets >= 4K / >= 60 Mbps
    // media). Coarse heuristics that should become user-configurable and metadata-driven
    // later. Original resolution is only known once a proxy entry exists, so never-proxied
    // clips fall back to the file-size floor as a rough bitrate x duration proxy.
    private const long MinBulkSourcePixelCount = 1920L * 1080L;
    private const long MinBulkSourceFileBytes = 32L * 1024L * 1024L;

    // Explicit per-clip / per-selection generate & regenerate is foreground work: a clip the editor
    // needs now must jump ahead of the background "Generate all" sweep, which stays at the queue
    // default so equal-priority bulk jobs keep arrival order.
    private const int ForegroundGenerationPriority = 1;
    private const int BulkGenerationPriority = 0;

    // Matches ProxyResolver.DensityTolerance so the tab's offline ranking clamps identically.
    private const float DensityTolerance = 1e-6f;

    private readonly CompositeDisposable _disposables = [];
    private readonly SerialDisposable _sceneEditedSubscriptions = new();
    private readonly Scene _scene;
    private readonly IProxyStore? _store;
    private readonly IProxyJobQueue? _queue;
    private readonly IProxyStoreCapInfo? _storeCapInfo;
    private readonly ProxyStoreConfig _config;
    private bool _isDisposed;
    private int _refreshScheduled;

    public ProxiesTabViewModel(IEditorContext editorContext)
    {
        _scene = editorContext.GetService<Scene>()!;
        _store = editorContext.GetService<IProxyStore>();
        _queue = editorContext.GetService<IProxyJobQueue>();
        _storeCapInfo = editorContext.GetService<IProxyStoreCapInfo>();
        _config = GlobalConfiguration.Instance.ProxyStoreConfig;

        ClipSummary = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        SelectionSummary = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        JobSummary = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        ProjectUsageText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        StoreUsageText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        StoreCapText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        StoreSummary = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        StatusMessage = new ReactiveProperty<string>(Strings.ProxyReady)
            .DisposeWith(_disposables);

        GenerateSelectedCommand = new AsyncReactiveCommand()
            .WithSubscribe(GenerateSelectedAsync)
            .DisposeWith(_disposables);
        RegenerateSelectedCommand = new AsyncReactiveCommand()
            .WithSubscribe(RegenerateSelectedAsync)
            .DisposeWith(_disposables);
        DeleteSelectedCommand = new ReactiveCommand()
            .WithSubscribe(DeleteSelected)
            .DisposeWith(_disposables);
        GenerateAllCommand = new AsyncReactiveCommand()
            .WithSubscribe(GenerateAllAsync)
            .DisposeWith(_disposables);
        DeleteAllForProjectCommand = new AsyncReactiveCommand()
            .WithSubscribe(DeleteAllForProjectAsync)
            .DisposeWith(_disposables);
        RefreshCommand = new ReactiveCommand()
            .WithSubscribe(Refresh)
            .DisposeWith(_disposables);

        if (_store != null)
        {
            _store.Changed += OnStoreChanged;
            Disposable.Create(() => _store.Changed -= OnStoreChanged)
                .DisposeWith(_disposables);
        }

        if (_queue != null)
        {
            _queue.JobChanged += OnJobChanged;
            Disposable.Create(() => _queue.JobChanged -= OnJobChanged)
                .DisposeWith(_disposables);
        }

        // Media can be added, removed, or replaced while the tab is open; Scene.Edited fires for
        // both structural child changes and forwarded element edits, so Generate All / Delete never
        // act on a stale clip list. ScheduleRefresh coalesces an edit burst into one rebuild.
        if (_scene != null)
        {
            _sceneEditedSubscriptions.DisposeWith(_disposables);
            RefreshSceneSubscriptions();

            // Project-wide totals / Generate All / Delete scan every project scene, so a media edit
            // in another open scene must also refresh; watch the project's scene set for add/remove.
            if (_scene.FindHierarchicalParent<Project>() is { } project)
            {
                project.Items.CollectionChanged += OnProjectItemsChanged;
                Disposable.Create(() => project.Items.CollectionChanged -= OnProjectItemsChanged)
                    .DisposeWith(_disposables);
            }
        }

        _config.PropertyChanged += OnProxyConfigPropertyChanged;
        Disposable.Create(() => _config.PropertyChanged -= OnProxyConfigPropertyChanged)
            .DisposeWith(_disposables);

        Refresh();
    }

    private void OnSceneEdited(object? sender, EventArgs e) => ScheduleRefresh();

    private void OnProjectItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshSceneSubscriptions();
        ScheduleRefresh();
    }

    private void RefreshSceneSubscriptions()
    {
        var subscriptions = new CompositeDisposable();
        foreach (Scene scene in EnumerateProjectScenes())
        {
            Scene captured = scene;
            captured.Edited += OnSceneEdited;
            subscriptions.Add(Disposable.Create(() => captured.Edited -= OnSceneEdited));
        }

        _sceneEditedSubscriptions.Disposable = subscriptions;
    }

    private void OnProxyConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e is CorePropertyChangedEventArgs<int> presetArgs
            && presetArgs.Property == ProxyStoreConfig.DefaultPresetProperty)
        {
            if (!Dispatcher.UIThread.CheckAccess())
                Dispatcher.UIThread.Post(() => ApplyDefaultPresetChange(presetArgs.OldValue, presetArgs.NewValue));
            else
                ApplyDefaultPresetChange(presetArgs.OldValue, presetArgs.NewValue);

            return;
        }

        // A cap-only change shifts the store summary (StoreCapText / usage), which no other signal on
        // this tab reflects until an unrelated store / job / scene refresh; refresh it here.
        if (e is CorePropertyChangedEventArgs<long> capArgs
            && capArgs.Property == ProxyStoreConfig.MaxTotalBytesProperty)
        {
            if (!Dispatcher.UIThread.CheckAccess())
                Dispatcher.UIThread.Post(UpdateStoreSummaryIfLive);
            else
                UpdateStoreSummaryIfLive();
        }
    }

    private void UpdateStoreSummaryIfLive()
    {
        if (!_isDisposed)
            UpdateStoreSummary();
    }

    // Rows that merely showed the previous default follow a Settings change, so Generate honors the
    // setting the UI now shows; rows the user explicitly set to another preset keep their choice.
    private void ApplyDefaultPresetChange(int oldValue, int newValue)
    {
        if (_isDisposed)
            return;

        ProxyPreset oldPreset = ToPreset(oldValue);
        ProxyPreset newPreset = ToPreset(newValue);
        if (oldPreset == newPreset)
            return;

        foreach (ProxyClipViewModel clip in Clips)
        {
            if (clip.IsFollowingDefault)
                clip.ApplyDefaultPreset(newPreset);
        }
    }

    public ObservableCollection<ProxyClipViewModel> Clips { get; } = [];

    public ReactiveProperty<string> ClipSummary { get; }

    public ReactiveProperty<string> SelectionSummary { get; }

    public ReactiveProperty<string> JobSummary { get; }

    public ReactiveProperty<string> ProjectUsageText { get; }

    public ReactiveProperty<string> StoreUsageText { get; }

    public ReactiveProperty<string> StoreCapText { get; }

    public ReactiveProperty<string> StoreSummary { get; }

    public ReactiveProperty<string> StatusMessage { get; }

    public AsyncReactiveCommand GenerateSelectedCommand { get; }

    public AsyncReactiveCommand RegenerateSelectedCommand { get; }

    public ReactiveCommand DeleteSelectedCommand { get; }

    public AsyncReactiveCommand GenerateAllCommand { get; }

    public AsyncReactiveCommand DeleteAllForProjectCommand { get; }

    public ReactiveCommand RefreshCommand { get; }

    /// <summary>
    /// Confirms the destructive "delete all proxies for this project" action. The proxy store
    /// is a machine-wide shared cache (FR-011), so the entries removed here may also be relied
    /// on by other projects that reference the same source files; those projects must then
    /// regenerate them. The argument is the number of cached proxy entries that would be
    /// deleted. The default shows a confirmation dialog; tests substitute it to drive the
    /// accept and decline paths without a UI. Returns <see langword="true"/> to proceed.
    /// </summary>
    public Func<int, Task<bool>> ConfirmDeleteAllForProjectAsync { get; set; }
        = ShowDeleteAllForProjectConfirmationAsync;

    public ToolTabExtension Extension => ProxiesTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public string Header => Strings.Proxies;

    public async Task GenerateAsync(ProxyClipViewModel clip)
    {
        if (_queue == null)
        {
            StatusMessage.Value = Strings.ProxyQueueUnavailable;
            return;
        }

        await _queue.EnqueueAsync(clip.Source, clip.Preset.Value, ForegroundGenerationPriority);
        RefreshJobs();
    }

    public async Task RegenerateAsync(ProxyClipViewModel clip)
    {
        await GenerateAsync(clip);
    }

    public void Delete(ProxyClipViewModel clip)
    {
        CancelMatchingJobs(clip);
        int failed = TryDeleteEntry(clip.EntrySource ?? clip.Source, clip.Preset.Value) ? 0 : 1;
        ReportDeleteFailures(failed);
        Refresh();
    }

    // Two shapes of a real failure are surfaced, both typically a sharing violation while the preview
    // decodes that proxy: Delete returns false with the entry still present, or Delete returns true (index
    // removed) yet its best-effort file delete left the .mp4 on disk. Delete returning false for an
    // already-gone entry is a benign race (eviction / another instance), not surfaced.
    private bool TryDeleteEntry(ProxyFingerprint source, ProxyPreset preset)
    {
        if (_store is not { } store)
            return true;

        string? proxyPath = ResolveProxyFilePath(store, source, preset);

        if (store.Delete(source, preset))
            return proxyPath is null || !File.Exists(proxyPath);

        return store.TryGet(source, preset) is null;
    }

    // The proxy file's absolute path from its store entry, captured before Delete removes the entry so a
    // surviving orphan can be detected afterward. Null when no entry exists to resolve.
    private static string? ResolveProxyFilePath(IProxyStore store, ProxyFingerprint source, ProxyPreset preset)
    {
        if (store.TryGet(source, preset) is not { } entry)
            return null;

        return Path.Combine(store.StoreRootPath, entry.ProxyFileRelative.Replace('/', Path.DirectorySeparatorChar));
    }

    private void ReportDeleteFailures(int failed)
    {
        if (failed <= 0)
            return;

        StatusMessage.Value = failed == 1
            ? Strings.ProxyDeleteFailedSingular
            : string.Format(CultureInfo.CurrentCulture, Strings.ProxyDeleteFailedPluralFormat, failed);
    }

    // A queued/running generation would Register the proxy again on success and silently undo the
    // delete; cancel it first. A stale row's job may be keyed on the old EntrySource fingerprint while
    // the current Source differs (the media file changed), so cancel both.
    private void CancelMatchingJobs(ProxyClipViewModel clip)
    {
        CancelMatchingJobs(clip.Source, clip.Preset.Value);
        if (clip.EntrySource is { } entrySource && entrySource != clip.Source)
            CancelMatchingJobs(entrySource, clip.Preset.Value);
    }

    private void CancelMatchingJobs(ProxyFingerprint source, ProxyPreset preset)
    {
        if (_queue == null)
            return;

        foreach (ProxyJob job in _queue.Pending())
        {
            if (job.Source == source && job.Preset == preset)
                _queue.Cancel(job.JobId);
        }
    }

    public void CancelJob(Guid jobId)
    {
        _queue?.Cancel(jobId);
        RefreshJobs();
    }

    internal void CancelJob(ProxyClipViewModel clip)
    {
        if (clip.JobId is not { } jobId)
            return;

        CancelJob(jobId);
    }

    internal void RefreshClip(ProxyClipViewModel clip)
    {
        clip.UpdateEntry(FindEntry(clip.Source, clip.Preset.Value));
        RefreshJobs();
        UpdateClipSummary();
    }

    internal void OnPresetChanged(ProxyClipViewModel clip, ProxyPreset oldPreset, ProxyPreset newPreset)
    {
        // Moving the dropdown off a preset whose job is still Queued/Running would otherwise
        // orphan that job: it keeps the sole serial slot and produces a proxy for a preset the
        // user no longer selected. Each source maps to a single row here, so cancelling the job
        // for (this source, old preset) cannot affect another visible clip.
        if (oldPreset != newPreset)
            CancelJobForSourcePreset(clip.Source, oldPreset);

        RefreshClip(clip);
    }

    private void CancelJobForSourcePreset(ProxyFingerprint source, ProxyPreset preset)
    {
        if (_queue == null)
            return;

        foreach (ProxyJob job in _queue.Pending())
        {
            if (job.Preset == preset && job.Source.Equals(source))
                _queue.Cancel(job.JobId);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        ClearClips();
        _disposables.Dispose();
    }

    public void WriteToJson(System.Text.Json.Nodes.JsonObject json)
    {
    }

    public void ReadFromJson(System.Text.Json.Nodes.JsonObject json)
    {
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }

    private async Task GenerateAllAsync()
    {
        if (_queue == null)
        {
            StatusMessage.Value = Strings.ProxyQueueUnavailable;
            return;
        }

        ProxyClipViewModel[] eligible = [.. Clips.Where(IsEligibleForBulkGeneration)];
        if (eligible.Length == 0)
        {
            // An all-light project would otherwise no-op silently on an explicit action; tell the
            // user nothing met the heaviness floor and point them at per-clip generate.
            if (Clips.Count > 0)
                StatusMessage.Value = Strings.ProxyBulkNoEligibleClips;

            return;
        }

        foreach (ProxyClipViewModel clip in eligible)
        {
            await _queue.EnqueueAsync(clip.Source, clip.Preset.Value, BulkGenerationPriority);
        }

        RefreshJobs();
    }

    private bool IsEligibleForBulkGeneration(ProxyClipViewModel clip)
    {
        if (TryGetSourcePixelCount(clip.Source) is { } pixelCount)
            return pixelCount >= MinBulkSourcePixelCount;

        return clip.Source.FileSizeBytes >= MinBulkSourceFileBytes;
    }

    private long? TryGetSourcePixelCount(ProxyFingerprint source)
    {
        if (_store == null)
            return null;

        foreach (ProxyEntry entry in _store.Enumerate())
        {
            // Match on path + size, not the full fingerprint: an in-place replace changes the size,
            // so old dimensions can't drive eligibility for a heavier file; a benign mtime-only
            // touch keeps the size, so valid dimensions are still used (rather than falling back to
            // the coarse file-size floor). Only same-path/same-size entries describe this file.
            if (entry.Source.AbsolutePath != source.AbsolutePath
                || entry.Source.FileSizeBytes != source.FileSizeBytes)
            {
                continue;
            }

            PixelSize frameSize = entry.OriginalLogicalFrameSize;
            if (frameSize.Width > 0 && frameSize.Height > 0)
                return (long)frameSize.Width * frameSize.Height;
        }

        return null;
    }

    private async Task GenerateSelectedAsync()
    {
        if (_queue == null)
        {
            StatusMessage.Value = Strings.ProxyQueueUnavailable;
            return;
        }

        foreach (ProxyClipViewModel clip in Clips.Where(static c => c.IsSelected.Value).ToArray())
        {
            await _queue.EnqueueAsync(clip.Source, clip.Preset.Value, ForegroundGenerationPriority);
        }

        RefreshJobs();
    }

    private async Task RegenerateSelectedAsync()
    {
        if (_queue == null)
        {
            StatusMessage.Value = Strings.ProxyQueueUnavailable;
            return;
        }

        foreach (ProxyClipViewModel clip in Clips.Where(static c => c.IsSelected.Value).ToArray())
        {
            await _queue.EnqueueAsync(clip.Source, clip.Preset.Value, ForegroundGenerationPriority);
        }

        Refresh();
    }

    private void DeleteSelected()
    {
        int failed = 0;
        foreach (ProxyClipViewModel clip in Clips.Where(static c => c.IsSelected.Value).ToArray())
        {
            CancelMatchingJobs(clip);
            if (!TryDeleteEntry(clip.EntrySource ?? clip.Source, clip.Preset.Value))
                failed++;
        }

        ReportDeleteFailures(failed);
        Refresh();
    }

    private async Task DeleteAllForProjectAsync()
    {
        if (_store == null)
            return;

        (ProxyEntry[] entries, ProxyJob[] projectJobs) = CollectProjectProxies();
        if (entries.Length == 0 && projectJobs.Length == 0)
            return;

        if (!await ConfirmDeleteAllForProjectAsync(entries.Length))
            return;

        // Re-snapshot after the dialog: a project job can finish while it is open and register a new
        // proxy the pre-dialog snapshot would miss, leaving it behind after the user confirms.
        (entries, projectJobs) = CollectProjectProxies();

        foreach (ProxyJob job in projectJobs)
        {
            _queue?.Cancel(job.JobId);
        }

        int failed = 0;
        foreach (ProxyEntry entry in entries)
        {
            if (!TryDeleteEntry(entry.Source, entry.Preset))
                failed++;
        }

        ReportDeleteFailures(failed);
        Refresh();
    }

    private (ProxyEntry[] Entries, ProxyJob[] Jobs) CollectProjectProxies()
    {
        HashSet<string> projectPaths = [.. Clips.Select(static c => c.Source.AbsolutePath)];
        ProxyEntry[] entries = _store == null
            ? []
            : [.. _store.Enumerate().Where(entry => projectPaths.Contains(entry.Source.AbsolutePath))];
        ProxyJob[] jobs = _queue == null
            ? []
            : [.. _queue.Pending().Where(job => projectPaths.Contains(job.Source.AbsolutePath))];
        return (entries, jobs);
    }

    private static async Task<bool> ShowDeleteAllForProjectConfirmationAsync(int entryCount)
    {
        var dialog = new ContentDialog
        {
            Title = Strings.ProxyDeleteProjectProxies,
            Content = string.Format(
                CultureInfo.CurrentCulture,
                Strings.ProxyDeleteProjectProxiesConfirmationFormat,
                entryCount),
            PrimaryButtonText = Strings.Yes,
            CloseButtonText = Strings.No,
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // Collapse a burst of store/job events (e.g. a "Generate all" completing N clips raises ~2N
    // events) into one full rebuild per UI tick; the heavy Refresh walks the whole project graph and
    // stats every source, so running it per event scales the timeline stall with clip count.
    private void ScheduleRefresh()
    {
        if (_isDisposed)
            return;

        if (Interlocked.Exchange(ref _refreshScheduled, 1) == 1)
            return;

        RefreshScheduler(() =>
        {
            Interlocked.Exchange(ref _refreshScheduled, 0);
            if (!_isDisposed)
                Refresh();
        });
    }

    // Test seam: the unit-test environment has no pumping dispatcher, so tests inject an
    // immediate scheduler to exercise the coalesced event-driven rebuild synchronously.
    internal Action<Action> RefreshScheduler { get; set; } = static action => Dispatcher.UIThread.Post(action);

    private void Refresh()
    {
        if (_isDisposed)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_isDisposed)
                    Refresh();
            });
            return;
        }

        // Only a deliberate user choice survives a rebuild verbatim; a background proxy finishing
        // (Registered/StateChanged) or a terminal job triggers Refresh, and an explicit pick must not be
        // reverted to following. A row that was merely pinned to an existing proxy re-derives its state
        // below so a since-deleted/evicted proxy no longer leaves it stuck off the default. Key on the
        // canonical Source.AbsolutePath (what enumeration dedupes on), not clip.Path (a raw LocalPath):
        // the same file spelled differently (case / symlink) must still re-associate its prior state.
        Dictionary<string, (ProxyPreset Preset, bool Explicit)> priorChoiceByKey = Clips.ToDictionary(
            static clip => clip.Source.AbsolutePath,
            static clip => (clip.Preset.Value, clip.IsExplicitlyChosen));
        HashSet<string> selectedKeys =
            [.. Clips.Where(static clip => clip.IsSelected.Value).Select(static clip => clip.Source.AbsolutePath)];

        ClearClips();
        foreach ((string path, ProxyFingerprint fingerprint) in EnumerateProjectVideoSources())
        {
            string key = fingerprint.AbsolutePath;
            ProxyPreset preset;
            bool followingDefault;
            bool explicitlyChosen;
            if (priorChoiceByKey.TryGetValue(key, out (ProxyPreset Preset, bool Explicit) prior) && prior.Explicit)
            {
                preset = prior.Preset;
                followingDefault = false;
                explicitlyChosen = true;
            }
            else
            {
                (preset, followingDefault) = ResolveInitialPreset(fingerprint);
                explicitlyChosen = false;
            }

            ProxyEntry? entry = FindEntry(fingerprint, preset);
            var clip = new ProxyClipViewModel(this, path, fingerprint, preset, entry, followingDefault, explicitlyChosen);
            if (selectedKeys.Contains(key))
                clip.IsSelected.Value = true;
            Clips.Add(clip);
        }

        RefreshJobs();
        UpdateStoreSummary();
        UpdateClipSummary();
    }

    private void RefreshJobs()
    {
        if (_isDisposed)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_isDisposed)
                    RefreshJobs();
            });
            return;
        }

        ProxyJob[] pendingJobs = [.. _queue?.Pending() ?? []];
        foreach (ProxyClipViewModel clip in Clips)
        {
            clip.UpdateJob(pendingJobs.FirstOrDefault(job => IsMatchingJob(job, clip)));
        }

        JobSummary.Value = pendingJobs.Length == 0
            ? Strings.ProxyQueueIdle
            : pendingJobs.Length == 1
                ? Strings.ProxyQueuedJobSingular
                : string.Format(CultureInfo.CurrentCulture, Strings.ProxyQueuedJobPlural, pendingJobs.Length);
    }

    /// <summary>
    /// The authoritative enumeration of the video sources referenced by the current project.
    /// Its result is the single source of truth for both the per-project usage total
    /// (<see cref="UpdateStoreSummary"/>) and the delete-all action
    /// (<see cref="DeleteAllForProjectAsync"/>) — both consume it via <see cref="Clips"/>.
    /// Any new element type that can hold a <see cref="VideoSource"/> MUST be walked here,
    /// otherwise its sources are silently excluded from usage accounting and delete-all.
    /// </summary>
    private IEnumerable<(string Path, ProxyFingerprint Fingerprint)> EnumerateProjectVideoSources()
    {
        HashSet<string> seenPaths = new(StringComparer.Ordinal);
        HashSet<Scene> seenScenes = new(ReferenceEqualityComparer.Instance);
        HashSet<(Scene, CompositionTarget?)> visitedRefScenes = [];
        ProxyEntry[] storeEntries = [.. _store?.Enumerate() ?? []];
        ProxyPreset preferredPreset = ToPreset(_config.DefaultPreset);

        // The totals and the delete action are labelled project-wide, so scan every scene in the
        // open project (not just the edited one); a clip used only by another scene must count too.
        foreach (Scene scene in EnumerateProjectScenes())
        {
            if (!seenScenes.Add(scene))
                continue;

            foreach (Element element in scene.Children)
            {
                foreach (VideoSource source in ProxySourceEnumerator.EnumerateVideoSources(element, visitedRefScenes))
                {
                    if (TryGetVideoSource(source, storeEntries, seenPaths, preferredPreset, out var item))
                        yield return item;
                }
            }
        }
    }

    private IEnumerable<Scene> EnumerateProjectScenes()
    {
        if (_scene.FindHierarchicalParent<Project>() is { } project)
        {
            foreach (Scene scene in project.Items.OfType<Scene>())
                yield return scene;
        }
        else
        {
            yield return _scene;
        }
    }

    private static bool TryGetVideoSource(
        VideoSource? source,
        IReadOnlyList<ProxyEntry> storeEntries,
        HashSet<string> seenPaths,
        ProxyPreset preferredPreset,
        out (string Path, ProxyFingerprint Fingerprint) item)
    {
        if (source is not { HasUri: true } || source.Uri is not { IsFile: true } uri)
        {
            item = default;
            return false;
        }

        string path = uri.LocalPath;
        if (ProxyFingerprint.TryFromFile(path, out ProxyFingerprint fingerprint))
        {
            if (!seenPaths.Add(fingerprint.AbsolutePath))
            {
                item = default;
                return false;
            }

            item = (path, fingerprint);
            return true;
        }

        string normalizedPath = NormalizeSourcePath(path);
        ProxyEntry? existing = SelectOfflineEntry(
            storeEntries.Where(entry => entry.Source.AbsolutePath == normalizedPath),
            preferredPreset);
        if (existing == null || !seenPaths.Add(existing.Source.AbsolutePath))
        {
            item = default;
            return false;
        }

        item = (path, existing.Source);
        return true;
    }

    // Pick the offline row's entry the same way preview decoding will. Among path-matching Ready
    // entries, mirror ProxyResolver.SelectBest: prefer the densest whose supply density is within the
    // preferred-preset cap, else the densest overall — so the row binds to the fingerprint playback
    // actually decodes. Only when no Ready proxy exists does it fall back to stale/failed metadata
    // (ranked by state) so the offline clip still surfaces a row.
    private static ProxyEntry? SelectOfflineEntry(IEnumerable<ProxyEntry> pathMatches, ProxyPreset preferredPreset)
    {
        List<ProxyEntry> candidates = [.. pathMatches];
        if (candidates.Count == 0)
            return null;

        // Choose the newest source version across ALL candidates (any state), then confine the whole
        // selection to that source — mirroring ProxyResolver.ResolveByPath. A newer Failed/Stale entry for
        // a replaced source must outrank an older Ready proxy of a stale fingerprint, so the row reflects
        // the current source's state instead of binding delete/regenerate to content preview won't decode.
        // Precompute newest generation per source once (a linear group) rather than re-scanning inside
        // the sort comparer, which would be O(n²) on a path with many accumulated versions/presets.
        Dictionary<ProxyFingerprint, DateTime> newestBySource = candidates
            .GroupBy(e => e.Source)
            .ToDictionary(g => g.Key, g => g.Max(e => e.GeneratedAtUtc));
        ProxyFingerprint newest = candidates
            .OrderByDescending(e => newestBySource[e.Source])
            .ThenByDescending(e => e.Source.MtimeUtc)
            .First().Source;
        List<ProxyEntry> fromNewest = [.. candidates.Where(e => e.Source == newest)];

        float cap = ProxyPresetDefinitions.Get(preferredPreset).Scale;
        ProxyEntry? cappedWinner = null;
        float cappedDensity = -1f;
        ProxyEntry? densestWinner = null;
        float densestDensity = -1f;
        foreach (ProxyEntry entry in fromNewest)
        {
            if (entry.State != ProxyState.Ready)
                continue;

            float density = SupplyDensityOf(entry);
            if (density <= cap + DensityTolerance && density > cappedDensity)
            {
                cappedWinner = entry;
                cappedDensity = density;
            }

            if (density > densestDensity)
            {
                densestWinner = entry;
                densestDensity = density;
            }
        }

        return cappedWinner ?? densestWinner ?? fromNewest
            .OrderBy(entry => OfflineEntryRank(entry.State))
            .ThenByDescending(entry => (long)entry.ProxyDecodedFrameSize.Width * entry.ProxyDecodedFrameSize.Height)
            .ThenByDescending(entry => entry.GeneratedAtUtc)
            .FirstOrDefault();
    }

    // Mirrors ProxyResolution.SupplyDensity (long-edge ratio) so the tab ranks Ready entries exactly as
    // the resolver does; ProxyEntry already stores both frame sizes.
    private static float SupplyDensityOf(ProxyEntry entry)
    {
        int originalLongEdge = Math.Max(entry.OriginalLogicalFrameSize.Width, entry.OriginalLogicalFrameSize.Height);
        int proxyLongEdge = Math.Max(entry.ProxyDecodedFrameSize.Width, entry.ProxyDecodedFrameSize.Height);
        return originalLongEdge == 0 || proxyLongEdge == 0
            ? 1f
            : (float)proxyLongEdge / originalLongEdge;
    }

    // Ready first (a usable stand-in), then Stale, then in-progress, then failed/absent last. Mirrors
    // ProxyResolver's offline preference so the tab and the preview agree on which entry wins.
    private static int OfflineEntryRank(ProxyState state) => state switch
    {
        ProxyState.Ready => 0,
        ProxyState.Stale => 1,
        ProxyState.Partial => 2,
        ProxyState.Generating => 3,
        ProxyState.Failed => 4,
        _ => 5,
    };

    private static string NormalizeSourcePath(string path)
    {
        // Resolve a symlink to the target path the store entry was keyed on (FromFile resolves the
        // link at registration). A moved/deleted target leaves the link resolvable via
        // returnFinalTarget:false, so a broken symlink still matches its stored entry instead of
        // dropping the clip from the tab. Case folding must match ProxyFingerprint.NormalizeAbsolutePath.
        string fullPath = ResolveLinkTarget(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? fullPath.ToUpperInvariant()
            : fullPath;
    }

    private static string ResolveLinkTarget(string fullPath)
    {
        try
        {
            return new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: false)?.FullName ?? fullPath;
        }
        catch
        {
            return fullPath;
        }
    }

    private void UpdateStoreSummary()
    {
        if (_store == null)
        {
            ProjectUsageText.Value = Strings.ProxyUnavailable;
            StoreUsageText.Value = Strings.ProxyUnavailable;
            StoreCapText.Value = Strings.ProxyUnavailable;
            StoreSummary.Value = Strings.ProxyStoreUnavailable;
            return;
        }

        HashSet<string> paths = [.. Clips.Select(static c => c.Source.AbsolutePath)];
        long projectBytes = _store.GetTotalBytes(paths);
        long totalBytes = _store.GetTotalBytes();
        ProjectUsageText.Value = FormatBytes(projectBytes);
        StoreUsageText.Value = FormatBytes(totalBytes);
        StoreCapText.Value = FormatBytes(_storeCapInfo?.MaxTotalBytes ?? _config.MaxTotalBytes);
        StoreSummary.Value = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ProxyStoreSummaryFormat,
            ProjectUsageText.Value,
            StoreUsageText.Value,
            StoreCapText.Value);
    }

    internal void UpdateClipSummary()
    {
        int ready = Clips.Count(static c => c.IsReady.Value);
        int stale = Clips.Count(static c => c.IsStale.Value);
        int failed = Clips.Count(static c => c.IsFailed.Value);
        int missing = Clips.Count(static c => c.IsMissing.Value);
        int selected = Clips.Count(static c => c.IsSelected.Value);

        string stateSummary = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ProxyClipSummaryFormat,
            Clips.Count,
            ready,
            stale,
            failed,
            missing);
        ClipSummary.Value = stateSummary;
        SelectionSummary.Value = selected == 1
            ? Strings.ProxySelectedSingular
            : string.Format(CultureInfo.CurrentCulture, Strings.ProxySelectedPlural, selected);
    }

    private ProxyEntry? FindEntry(ProxyFingerprint source, ProxyPreset preset)
    {
        if (_store == null)
            return null;

        if (_store.TryGet(source, preset) is { } exact)
            return exact;

        return _store.Enumerate()
            .Where(entry => entry.Preset == preset)
            .FirstOrDefault(entry => entry.Source.AbsolutePath == source.AbsolutePath);
    }

    // The preset a freshly listed row starts on, and whether that row tracks the global default. A row
    // pinned to an already-generated proxy (at the default preset or any other) does not follow the
    // default; only a row that fell through to the default with no generated proxy does.
    private (ProxyPreset Preset, bool FollowingDefault) ResolveInitialPreset(ProxyFingerprint source)
    {
        ProxyPreset defaultPreset = ToPreset(_config.DefaultPreset);
        if (IsGenerated(FindEntry(source, defaultPreset)))
            return (defaultPreset, false);

        foreach (ProxyPreset preset in s_presetOrder)
        {
            if (IsGenerated(FindEntry(source, preset)))
                return (preset, false);
        }

        return (defaultPreset, true);
    }

    private static bool IsGenerated(ProxyEntry? entry)
    {
        return entry?.State is ProxyState.Ready or ProxyState.Stale;
    }

    private void OnStoreChanged(object? sender, ProxyStoreChangedEventArgs e)
    {
        if (_isDisposed)
            return;

        // A Touched event (preview resolving a proxy) only bumps LastUsedUtc; rebuilding the clip
        // list on it would silently clear the user's bulk-action selection during normal playback.
        if (e.Kind == ProxyStoreChangeKind.Touched)
            return;

        ScheduleRefresh();
    }

    private void OnJobChanged(object? sender, ProxyJobChangedEventArgs e)
    {
        if (_isDisposed)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_isDisposed)
                    OnJobChanged(sender, e);
            });
            return;
        }

        StatusMessage.Value = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ProxyJobStatusWithMessageFormat,
            Path.GetFileName(e.Job.Source.AbsolutePath),
            GetJobStatusText(e.Job.Status));
        RefreshJobs();
        if (e.Kind is ProxyJobChangeKind.Succeeded
            or ProxyJobChangeKind.Failed
            or ProxyJobChangeKind.Canceled
            or ProxyJobChangeKind.Skipped)
        {
            ScheduleRefresh();
        }
    }

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    internal static string FormatSize(PixelSize size)
    {
        return $"{size.Width}x{size.Height}";
    }

    internal static string GetPresetDisplayName(ProxyPreset preset)
    {
        return preset switch
        {
            ProxyPreset.Half => Strings.ProxyPresetHalf,
            ProxyPreset.Quarter => Strings.ProxyPresetQuarter,
            ProxyPreset.Eighth => Strings.ProxyPresetEighth,
            _ => preset.ToString(),
        };
    }

    internal static string GetProxyStateText(ProxyState state)
    {
        return state switch
        {
            ProxyState.None => Strings.ProxyMissing,
            ProxyState.Generating => Strings.ProxyGenerating,
            ProxyState.Ready => Strings.ProxyReady,
            ProxyState.Stale => Strings.ProxyStale,
            ProxyState.Failed => Strings.ProxyFailed,
            ProxyState.Partial => Strings.ProxyPartial,
            _ => state.ToString(),
        };
    }

    internal static string GetJobStatusText(ProxyJobStatus status)
    {
        return status switch
        {
            ProxyJobStatus.Queued => Strings.ProxyJobStatusQueued,
            ProxyJobStatus.Running => Strings.ProxyJobStatusRunning,
            ProxyJobStatus.Succeeded => Strings.ProxyJobStatusSucceeded,
            ProxyJobStatus.Failed => Strings.ProxyJobStatusFailed,
            ProxyJobStatus.Canceled => Strings.ProxyJobStatusCanceled,
            ProxyJobStatus.Skipped => Strings.ProxyJobStatusSkipped,
            _ => status.ToString(),
        };
    }

    internal static string GetJobStatusText(ProxyJob job)
    {
        return string.IsNullOrWhiteSpace(job.StatusMessage)
            ? GetJobStatusText(job.Status)
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.ProxyJobStatusWithMessageFormat,
                GetJobStatusText(job.Status),
                job.StatusMessage);
    }

    internal static string FormatProgress(double fraction)
    {
        return Math.Clamp(fraction, 0, 1).ToString("P0", CultureInfo.CurrentCulture);
    }

    private static ProxyPreset ToPreset(int value)
    {
        return Enum.IsDefined(typeof(ProxyPreset), value)
            ? (ProxyPreset)value
            : ProxyPreset.Quarter;
    }

    private void ClearClips()
    {
        foreach (ProxyClipViewModel clip in Clips)
        {
            clip.Dispose();
        }

        Clips.Clear();
    }

    private static bool IsMatchingJob(ProxyJob job, ProxyClipViewModel clip)
        => job.Preset == clip.Preset.Value && job.Source.Equals(clip.Source);
}

public sealed class ProxyClipViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly ProxiesTabViewModel _owner;

    private bool _applyingDefault;

    public ProxyClipViewModel(
        ProxiesTabViewModel owner,
        string path,
        ProxyFingerprint source,
        ProxyPreset preset,
        ProxyEntry? entry,
        bool followingDefault,
        bool explicitlyChosen)
    {
        _owner = owner;
        Path = path;
        Source = source;
        IsFollowingDefault = followingDefault;
        IsExplicitlyChosen = explicitlyChosen;
        Preset = new ReactiveProperty<ProxyPreset>(preset)
            .DisposeWith(_disposables);
        State = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        ProxyInfoText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        LastUsedText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        IsReady = new ReactiveProperty<bool>()
            .DisposeWith(_disposables);
        IsStale = new ReactiveProperty<bool>()
            .DisposeWith(_disposables);
        IsFailed = new ReactiveProperty<bool>()
            .DisposeWith(_disposables);
        IsMissing = new ReactiveProperty<bool>()
            .DisposeWith(_disposables);
        SourceInfoText = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ProxySourceInfoFormat,
            ProxiesTabViewModel.FormatBytes(source.FileSizeBytes),
            source.MtimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
        HasJob = new ReactiveProperty<bool>()
            .DisposeWith(_disposables);
        JobStatus = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        JobProgressValue = new ReactiveProperty<double>()
            .DisposeWith(_disposables);
        JobProgressText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        IsSelected = new ReactiveProperty<bool>()
            .DisposeWith(_disposables);
        IsSelected.Subscribe(_ => _owner.UpdateClipSummary())
            .DisposeWith(_disposables);
        GenerateCommand = new AsyncReactiveCommand()
            .WithSubscribe(() => _owner.GenerateAsync(this))
            .DisposeWith(_disposables);
        RegenerateCommand = new AsyncReactiveCommand()
            .WithSubscribe(() => _owner.RegenerateAsync(this))
            .DisposeWith(_disposables);
        DeleteCommand = new ReactiveCommand()
            .WithSubscribe(() => _owner.Delete(this))
            .DisposeWith(_disposables);
        CancelJobCommand = new ReactiveCommand()
            .WithSubscribe(() => _owner.CancelJob(this))
            .DisposeWith(_disposables);

        bool initialized = false;
        ProxyPreset previousPreset = preset;
        Preset.Subscribe(newPreset =>
            {
                if (initialized)
                {
                    // A change the user drove from the dropdown is an explicit choice, so the row stops
                    // tracking the global default and records the choice so a later Refresh preserves it.
                    // A programmatic ApplyDefaultPreset write (guarded by _applyingDefault) keeps the row
                    // following.
                    if (!_applyingDefault)
                    {
                        IsFollowingDefault = false;
                        IsExplicitlyChosen = true;
                    }

                    _owner.OnPresetChanged(this, previousPreset, newPreset);
                }

                previousPreset = newPreset;
            })
            .DisposeWith(_disposables);
        UpdateEntry(entry);
        initialized = true;
    }

    // Whether this row still tracks the global default preset. A row starts following when its preset
    // came from the default with no pre-existing proxy pinning it; it stops the moment the user picks a
    // preset from the dropdown, even one that equals the current default.
    public bool IsFollowingDefault { get; private set; }

    // Set once the user picks a preset from the dropdown. Unlike IsFollowingDefault (which a pinned proxy
    // also clears), this records only a deliberate user choice, so Refresh preserves it across a rebuild
    // while a merely-pinned row re-derives its follow state — a proxy that later disappears reverts the
    // row to following the default instead of leaving it stuck on the stale preset.
    public bool IsExplicitlyChosen { get; private set; }

    // Push a global-default change onto a following row without clearing IsFollowingDefault. Distinct
    // from a user edit so a row whose explicit choice happens to equal the old default is not swept along.
    internal void ApplyDefaultPreset(ProxyPreset newPreset)
    {
        if (Preset.Value == newPreset)
            return;

        _applyingDefault = true;
        try
        {
            Preset.Value = newPreset;
        }
        finally
        {
            _applyingDefault = false;
        }
    }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string Path { get; }

    public ProxyFingerprint Source { get; }

    public ProxyFingerprint? EntrySource { get; private set; }

    public ReactiveProperty<ProxyPreset> Preset { get; }

    public ReactiveProperty<string> State { get; }

    public string SourceInfoText { get; }

    public ReactiveProperty<string> ProxyInfoText { get; }

    public ReactiveProperty<string> LastUsedText { get; }

    public ReactiveProperty<bool> IsReady { get; }

    public ReactiveProperty<bool> IsStale { get; }

    public ReactiveProperty<bool> IsFailed { get; }

    public ReactiveProperty<bool> IsMissing { get; }

    internal Guid? JobId { get; private set; }

    public ReactiveProperty<bool> HasJob { get; }

    public ReactiveProperty<string> JobStatus { get; }

    public ReactiveProperty<double> JobProgressValue { get; }

    public ReactiveProperty<string> JobProgressText { get; }

    public ReactiveProperty<bool> IsSelected { get; }

    public AsyncReactiveCommand GenerateCommand { get; }

    public AsyncReactiveCommand RegenerateCommand { get; }

    public ReactiveCommand DeleteCommand { get; }

    public ReactiveCommand CancelJobCommand { get; }

    internal void UpdateEntry(ProxyEntry? entry)
    {
        EntrySource = entry?.Source;
        ProxyState state = entry == null
            ? ProxyState.None
            : entry.Source == Source
                ? entry.State
                : ProxyState.Stale;
        State.Value = ProxiesTabViewModel.GetProxyStateText(state);
        IsReady.Value = state == ProxyState.Ready;
        IsStale.Value = state == ProxyState.Stale;
        IsFailed.Value = state == ProxyState.Failed;
        IsMissing.Value = state == ProxyState.None;
        ProxyInfoText.Value = entry == null
            ? Strings.ProxyMissingForSelectedPreset
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.ProxyInfoFormat,
                ProxiesTabViewModel.FormatSize(entry.OriginalLogicalFrameSize),
                ProxiesTabViewModel.FormatSize(entry.ProxyDecodedFrameSize),
                ProxiesTabViewModel.FormatBytes(entry.ProxyFileSizeBytes));
        LastUsedText.Value = entry == null
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.ProxyLastUsedFormat,
                entry.LastUsedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
    }

    internal void UpdateJob(ProxyJob? job)
    {
        JobId = job?.JobId;
        HasJob.Value = job != null;
        JobStatus.Value = job == null
            ? string.Empty
            : ProxiesTabViewModel.GetJobStatusText(job);
        JobProgressValue.Value = job?.LatestProgress?.FractionComplete ?? 0;
        JobProgressText.Value = job?.LatestProgress is { } progress
            ? ProxiesTabViewModel.FormatProgress(progress.FractionComplete)
            : string.Empty;
    }

    public void ToggleSelection()
    {
        IsSelected.Value = !IsSelected.Value;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
