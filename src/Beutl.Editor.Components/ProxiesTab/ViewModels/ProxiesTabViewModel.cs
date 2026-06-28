using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Disposables;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Editor.Components.ProxiesTab;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.ProjectSystem;
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
        DeleteAllForProjectCommand = new ReactiveCommand()
            .WithSubscribe(DeleteAllForProject)
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

    public ReactiveCommand DeleteAllForProjectCommand { get; }

    public ReactiveCommand RefreshCommand { get; }

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

        await _queue.EnqueueAsync(clip.Source, clip.Preset.Value);
        RefreshJobs();
    }

    public async Task RegenerateAsync(ProxyClipViewModel clip)
    {
        _store?.Delete(clip.EntrySource ?? clip.Source, clip.Preset.Value);
        await GenerateAsync(clip);
    }

    public void Delete(ProxyClipViewModel clip)
    {
        _store?.Delete(clip.EntrySource ?? clip.Source, clip.Preset.Value);
        Refresh();
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

        foreach (ProxyClipViewModel clip in Clips.ToArray())
        {
            await _queue.EnqueueAsync(clip.Source, clip.Preset.Value);
        }

        RefreshJobs();
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
            await _queue.EnqueueAsync(clip.Source, clip.Preset.Value);
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
            _store?.Delete(clip.EntrySource ?? clip.Source, clip.Preset.Value);
            await _queue.EnqueueAsync(clip.Source, clip.Preset.Value);
        }

        Refresh();
    }

    private void DeleteSelected()
    {
        foreach (ProxyClipViewModel clip in Clips.Where(static c => c.IsSelected.Value).ToArray())
        {
            _store?.Delete(clip.EntrySource ?? clip.Source, clip.Preset.Value);
        }

        Refresh();
    }

    private void DeleteAllForProject()
    {
        if (_store == null)
            return;

        HashSet<string> projectPaths = [.. Clips.Select(static c => c.Source.AbsolutePath)];
        foreach (ProxyEntry entry in _store.Enumerate()
                     .Where(entry => projectPaths.Contains(entry.Source.AbsolutePath))
                     .ToArray())
        {
            _store.Delete(entry.Source, entry.Preset);
        }

        Refresh();
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

        ClearClips();
        foreach ((string path, ProxyFingerprint fingerprint) in EnumerateVideoSources())
        {
            ProxyPreset preset = selectedPresets.GetValueOrDefault(path, FindDefaultPreset(fingerprint));
            ProxyEntry? entry = FindEntry(fingerprint, preset);
            Clips.Add(new ProxyClipViewModel(this, path, fingerprint, preset, entry));
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

    private IEnumerable<(string Path, ProxyFingerprint Fingerprint)> EnumerateVideoSources()
    {
        HashSet<ProxyFingerprint> seen = [];
        foreach (Element element in _scene.Children)
        {
            foreach (SourceVideo video in element.Objects.OfType<SourceVideo>())
            {
                Uri? uri = video.Source.CurrentValue?.Uri;
                if (uri is not { IsFile: true })
                    continue;

                string path = uri.LocalPath;
                if (!ProxyFingerprint.TryFromFile(path, out ProxyFingerprint fingerprint))
                    continue;

                if (seen.Add(fingerprint))
                    yield return (path, fingerprint);
            }
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
        Preset.Subscribe(_ =>
            {
                if (initialized)
                    _owner.RefreshClip(this);
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
