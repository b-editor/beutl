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
    private readonly CompositeDisposable _disposables = [];
    private readonly Scene _scene;
    private readonly IProxyStore? _store;
    private readonly IProxyJobQueue? _queue;
    private readonly ProxyStoreConfig _config;

    public ProxiesTabViewModel(IEditorContext editorContext)
    {
        _scene = editorContext.GetService<Scene>()!;
        _store = editorContext.GetService<IProxyStore>();
        _queue = editorContext.GetService<IProxyJobQueue>();
        _config = GlobalConfiguration.Instance.ProxyStoreConfig;

        SelectedPreset = new ReactiveProperty<ProxyPreset>(ToPreset(_config.DefaultPreset))
            .DisposeWith(_disposables);
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
        SelectedPresetDisplayText = new ReactiveProperty<string>(GetPresetDisplayName(SelectedPreset.Value))
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

        SelectedPreset.Subscribe(preset =>
            {
                SelectedPresetDisplayText.Value = GetPresetDisplayName(preset);
                Refresh();
            })
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

    public ObservableCollection<ProxyJobViewModel> PendingJobs { get; } = [];

    public ReactiveProperty<ProxyPreset> SelectedPreset { get; }

    public ReactiveProperty<string> ClipSummary { get; }

    public ReactiveProperty<string> SelectionSummary { get; }

    public ReactiveProperty<string> JobSummary { get; }

    public ReactiveProperty<string> ProjectUsageText { get; }

    public ReactiveProperty<string> StoreUsageText { get; }

    public ReactiveProperty<string> StoreCapText { get; }

    public ReactiveProperty<string> StoreSummary { get; }

    public ReactiveProperty<string> SelectedPresetDisplayText { get; }

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

        await _queue.EnqueueAsync(clip.Source, clip.Preset);
        RefreshJobs();
    }

    public async Task RegenerateAsync(ProxyClipViewModel clip)
    {
        _store?.Delete(clip.EntrySource ?? clip.Source, clip.Preset);
        await GenerateAsync(clip);
    }

    public void Delete(ProxyClipViewModel clip)
    {
        _store?.Delete(clip.EntrySource ?? clip.Source, clip.Preset);
        Refresh();
    }

    public void CancelJob(Guid jobId)
    {
        _queue?.Cancel(jobId);
        RefreshJobs();
    }

    public void Dispose()
    {
        ClearClips();
        ClearPendingJobs();
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
            await _queue.EnqueueAsync(clip.Source, clip.Preset);
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
            await _queue.EnqueueAsync(clip.Source, clip.Preset);
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
            _store?.Delete(clip.EntrySource ?? clip.Source, clip.Preset);
            await _queue.EnqueueAsync(clip.Source, clip.Preset);
        }

        Refresh();
    }

    private void DeleteSelected()
    {
        foreach (ProxyClipViewModel clip in Clips.Where(static c => c.IsSelected.Value).ToArray())
        {
            _store?.Delete(clip.EntrySource ?? clip.Source, clip.Preset);
        }

        Refresh();
    }

    private void DeleteAllForProject()
    {
        if (_store == null)
            return;

        foreach (ProxyFingerprint source in Clips.Select(static c => c.Source).Distinct().ToArray())
        {
            foreach (ProxyPreset preset in Enum.GetValues<ProxyPreset>())
            {
                _store.Delete(source, preset);
            }
        }

        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Refresh);
            return;
        }

        ClearClips();
        foreach ((string path, ProxyFingerprint fingerprint) in EnumerateVideoSources())
        {
            ProxyPreset preset = SelectedPreset.Value;
            ProxyEntry? entry = FindEntry(fingerprint, preset);
            Clips.Add(new ProxyClipViewModel(this, path, fingerprint, preset, entry));
        }

        RefreshJobs();
        UpdateStoreSummary();
        UpdateClipSummary();
    }

    private void RefreshJobs()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshJobs);
            return;
        }

        ClearPendingJobs();
        foreach (ProxyJob job in _queue?.Pending() ?? [])
        {
            PendingJobs.Add(new ProxyJobViewModel(this, job));
        }

        JobSummary.Value = PendingJobs.Count == 0
            ? Strings.ProxyQueueIdle
            : PendingJobs.Count == 1
                ? Strings.ProxyQueuedJobSingular
                : string.Format(CultureInfo.CurrentCulture, Strings.ProxyQueuedJobPlural, PendingJobs.Count);
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
        int ready = Clips.Count(static c => c.IsReady);
        int stale = Clips.Count(static c => c.IsStale);
        int failed = Clips.Count(static c => c.IsFailed);
        int missing = Clips.Count(static c => c.IsMissing);
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

    private void OnStoreChanged(object? sender, ProxyStoreChangedEventArgs e)
    {
        Refresh();
    }

    private void OnJobChanged(object? sender, ProxyJobChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnJobChanged(sender, e));
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

    private void ClearPendingJobs()
    {
        foreach (ProxyJobViewModel job in PendingJobs)
        {
            job.Dispose();
        }

        PendingJobs.Clear();
    }
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
        Preset = preset;
        EntrySource = entry?.Source;
        ProxyState state = entry == null
            ? ProxyState.None
            : entry.Source == source
                ? entry.State
                : ProxyState.Stale;
        State = ProxiesTabViewModel.GetProxyStateText(state);
        IsReady = state == ProxyState.Ready;
        IsStale = state == ProxyState.Stale;
        IsFailed = state == ProxyState.Failed;
        IsMissing = state == ProxyState.None;
        SourceInfoText = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ProxySourceInfoFormat,
            ProxiesTabViewModel.FormatBytes(source.FileSizeBytes),
            source.MtimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
        ProxyInfoText = entry == null
            ? Strings.ProxyMissingForSelectedPreset
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.ProxyInfoFormat,
                ProxiesTabViewModel.FormatSize(entry.OriginalLogicalFrameSize),
                ProxiesTabViewModel.FormatSize(entry.ProxyDecodedFrameSize),
                ProxiesTabViewModel.FormatBytes(entry.ProxyFileSizeBytes));
        LastUsedText = entry == null
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.ProxyLastUsedFormat,
                entry.LastUsedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
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
    }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string Path { get; }

    public ProxyFingerprint Source { get; }

    public ProxyFingerprint? EntrySource { get; }

    public ProxyPreset Preset { get; }

    public string State { get; }

    public string SourceInfoText { get; }

    public string ProxyInfoText { get; }

    public string LastUsedText { get; }

    public bool IsReady { get; }

    public bool IsStale { get; }

    public bool IsFailed { get; }

    public bool IsMissing { get; }

    public ReactiveProperty<bool> IsSelected { get; }

    public AsyncReactiveCommand GenerateCommand { get; }

    public AsyncReactiveCommand RegenerateCommand { get; }

    public ReactiveCommand DeleteCommand { get; }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}

public sealed class ProxyJobViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly ProxiesTabViewModel _owner;
    private readonly ProxyJob _job;

    public ProxyJobViewModel(ProxiesTabViewModel owner, ProxyJob job)
    {
        _owner = owner;
        _job = job;
        CancelCommand = new ReactiveCommand()
            .WithSubscribe(() => _owner.CancelJob(_job.JobId))
            .DisposeWith(_disposables);
    }

    public string FileName => Path.GetFileName(_job.Source.AbsolutePath);

    public string Preset => ProxiesTabViewModel.GetPresetDisplayName(_job.Preset);

    public string Status => string.IsNullOrWhiteSpace(_job.StatusMessage)
        ? ProxiesTabViewModel.GetJobStatusText(_job.Status)
        : string.Format(
            CultureInfo.CurrentCulture,
            Strings.ProxyJobStatusWithMessageFormat,
            ProxiesTabViewModel.GetJobStatusText(_job.Status),
            _job.StatusMessage);

    public double ProgressValue => _job.LatestProgress?.FractionComplete ?? 0;

    public string ProgressText => _job.LatestProgress is { } progress
        ? $"{Math.Clamp(progress.FractionComplete, 0, 1):P0}"
        : string.Empty;

    public ReactiveCommand CancelCommand { get; }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
