using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Disposables;
using Avalonia.Threading;
using Beutl.Animation;
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

    private readonly CompositeDisposable _disposables = [];
    private readonly Scene _scene;
    private readonly IProxyStore? _store;
    private readonly IProxyJobQueue? _queue;
    private readonly ProxyStoreConfig _config;
    private bool _isDisposed;

    public ProxiesTabViewModel(IEditorContext editorContext)
    {
        _scene = editorContext.GetService<Scene>()!;
        _store = editorContext.GetService<IProxyStore>();
        _queue = editorContext.GetService<IProxyJobQueue>();
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

        Refresh();
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
        _store?.Delete(clip.EntrySource ?? clip.Source, clip.Preset.Value);
        Refresh();
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
            if (entry.Source.AbsolutePath != source.AbsolutePath)
                continue;

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
        foreach (ProxyClipViewModel clip in Clips.Where(static c => c.IsSelected.Value).ToArray())
        {
            CancelMatchingJobs(clip);
            _store?.Delete(clip.EntrySource ?? clip.Source, clip.Preset.Value);
        }

        Refresh();
    }

    private async Task DeleteAllForProjectAsync()
    {
        if (_store == null)
            return;

        HashSet<string> projectPaths = [.. Clips.Select(static c => c.Source.AbsolutePath)];
        ProxyEntry[] entries =
        [
            .. _store.Enumerate().Where(entry => projectPaths.Contains(entry.Source.AbsolutePath)),
        ];
        ProxyJob[] projectJobs = _queue == null
            ? []
            : [.. _queue.Pending().Where(job => projectPaths.Contains(job.Source.AbsolutePath))];
        if (entries.Length == 0 && projectJobs.Length == 0)
            return;

        if (!await ConfirmDeleteAllForProjectAsync(entries.Length))
            return;

        foreach (ProxyJob job in projectJobs)
        {
            _queue?.Cancel(job.JobId);
        }

        foreach (ProxyEntry entry in entries)
        {
            _store.Delete(entry.Source, entry.Preset);
        }

        Refresh();
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

        Dictionary<string, ProxyPreset> selectedPresets = Clips.ToDictionary(
            static clip => clip.Path,
            static clip => clip.Preset.Value);
        // Carry the selection across the rebuild too: a background proxy finishing (Registered/
        // StateChanged) or terminal job triggers Refresh, and losing the selection would silently
        // drop clips the user had picked for a bulk generate/delete.
        HashSet<string> selectedPaths =
            [.. Clips.Where(static clip => clip.IsSelected.Value).Select(static clip => clip.Path)];

        ClearClips();
        foreach ((string path, ProxyFingerprint fingerprint) in EnumerateProjectVideoSources())
        {
            ProxyPreset preset = selectedPresets.GetValueOrDefault(path, FindDefaultPreset(fingerprint));
            ProxyEntry? entry = FindEntry(fingerprint, preset);
            var clip = new ProxyClipViewModel(this, path, fingerprint, preset, entry);
            if (selectedPaths.Contains(path))
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
        ProxyEntry[] storeEntries = [.. _store?.Enumerate() ?? []];

        // The totals and the delete action are labelled project-wide, so scan every scene in the
        // open project (not just the edited one); a clip used only by another scene must count too.
        foreach (Scene scene in EnumerateProjectScenes())
        {
            if (!seenScenes.Add(scene))
                continue;

            foreach (Element element in scene.Children)
            {
                foreach (VideoSource source in ProxySourceEnumerator.EnumerateVideoSources(element, seenScenes))
                {
                    if (TryGetVideoSource(source, storeEntries, seenPaths, out var item))
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
        ProxyEntry? existing = storeEntries.FirstOrDefault(entry => entry.Source.AbsolutePath == normalizedPath);
        if (existing == null || !seenPaths.Add(existing.Source.AbsolutePath))
        {
            item = default;
            return false;
        }

        item = (path, existing.Source);
        return true;
    }

    private static string NormalizeSourcePath(string path)
    {
        // Case folding must match ProxyFingerprint.NormalizeAbsolutePath (Windows + macOS).
        string fullPath = Path.GetFullPath(path);
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? fullPath.ToUpperInvariant()
            : fullPath;
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
        StoreCapText.Value = FormatBytes(_config.MaxTotalBytes);
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

    private ProxyPreset FindDefaultPreset(ProxyFingerprint source)
    {
        ProxyPreset defaultPreset = ToPreset(_config.DefaultPreset);
        if (IsGenerated(FindEntry(source, defaultPreset)))
            return defaultPreset;

        foreach (ProxyPreset preset in s_presetOrder)
        {
            if (IsGenerated(FindEntry(source, preset)))
                return preset;
        }

        return defaultPreset;
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

        Refresh();
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
            Refresh();
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

    public ProxyClipViewModel(
        ProxiesTabViewModel owner,
        string path,
        ProxyFingerprint source,
        ProxyPreset preset,
        ProxyEntry? entry)
    {
        _owner = owner;
        Path = path;
        Source = source;
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
                    _owner.OnPresetChanged(this, previousPreset, newPreset);

                previousPreset = newPreset;
            })
            .DisposeWith(_disposables);
        UpdateEntry(entry);
        initialized = true;
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
