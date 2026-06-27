using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using Avalonia.Threading;
using Beutl.Editor.Components.ProxiesTab;
using Beutl.Graphics;
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

    public ProxiesTabViewModel(IEditorContext editorContext)
    {
        _scene = editorContext.GetService<Scene>()!;
        _store = editorContext.GetService<IProxyStore>();
        _queue = editorContext.GetService<IProxyJobQueue>();

        SelectedPreset = new ReactiveProperty<ProxyPreset>(ProxyPreset.Quarter)
            .DisposeWith(_disposables);
        StoreSummary = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        StatusMessage = new ReactiveProperty<string>()
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

        SelectedPreset.Subscribe(_ => Refresh())
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

    public IReadOnlyList<ProxyPreset> PresetOptions { get; } = Enum.GetValues<ProxyPreset>();

    public ReactiveProperty<string> StoreSummary { get; }

    public ReactiveProperty<string> StatusMessage { get; }

    public AsyncReactiveCommand GenerateAllCommand { get; }

    public ReactiveCommand DeleteAllForProjectCommand { get; }

    public ReactiveCommand RefreshCommand { get; }

    public ToolTabExtension Extension => ProxiesTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public string Header => "Proxies";

    public async Task GenerateAsync(ProxyClipViewModel clip)
    {
        if (_queue == null)
        {
            StatusMessage.Value = "Proxy queue is not available.";
            return;
        }

        await _queue.EnqueueAsync(clip.Source, clip.Preset);
        RefreshJobs();
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
        _disposables.Dispose();
        Clips.Clear();
        PendingJobs.Clear();
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
            StatusMessage.Value = "Proxy queue is not available.";
            return;
        }

        foreach (ProxyClipViewModel clip in Clips.ToArray())
        {
            await _queue.EnqueueAsync(clip.Source, clip.Preset);
        }

        RefreshJobs();
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

        Clips.Clear();
        foreach ((string path, ProxyFingerprint fingerprint) in EnumerateVideoSources())
        {
            ProxyPreset preset = SelectedPreset.Value;
            ProxyEntry? entry = FindEntry(fingerprint, preset);
            Clips.Add(new ProxyClipViewModel(this, path, fingerprint, preset, entry));
        }

        RefreshJobs();
        UpdateStoreSummary();
    }

    private void RefreshJobs()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshJobs);
            return;
        }

        PendingJobs.Clear();
        foreach (ProxyJob job in _queue?.Pending() ?? [])
        {
            PendingJobs.Add(new ProxyJobViewModel(this, job));
        }
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
            StoreSummary.Value = "Proxy store is not available.";
            return;
        }

        HashSet<string> paths = [.. Clips.Select(static c => c.Source.AbsolutePath)];
        long projectBytes = _store.GetTotalBytes(paths);
        long totalBytes = _store.GetTotalBytes();
        StoreSummary.Value = $"Project {FormatBytes(projectBytes)} / Store {FormatBytes(totalBytes)}";
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

        StatusMessage.Value = $"{Path.GetFileName(e.Job.Source.AbsolutePath)}: {e.Job.Status}";
        RefreshJobs();
        if (e.Kind is ProxyJobChangeKind.Succeeded
            or ProxyJobChangeKind.Failed
            or ProxyJobChangeKind.Canceled
            or ProxyJobChangeKind.Skipped)
        {
            Refresh();
        }
    }

    private static string FormatBytes(long bytes)
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
}

public sealed class ProxyClipViewModel
{
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
        State = entry == null
            ? ProxyState.None.ToString()
            : entry.Source == source
                ? entry.State.ToString()
                : ProxyState.Stale.ToString();
        GenerateCommand = new AsyncReactiveCommand()
            .WithSubscribe(() => _owner.GenerateAsync(this));
        DeleteCommand = new ReactiveCommand()
            .WithSubscribe(() => _owner.Delete(this));
    }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string Path { get; }

    public ProxyFingerprint Source { get; }

    public ProxyFingerprint? EntrySource { get; }

    public ProxyPreset Preset { get; }

    public string State { get; }

    public AsyncReactiveCommand GenerateCommand { get; }

    public ReactiveCommand DeleteCommand { get; }
}

public sealed class ProxyJobViewModel
{
    private readonly ProxiesTabViewModel _owner;
    private readonly ProxyJob _job;

    public ProxyJobViewModel(ProxiesTabViewModel owner, ProxyJob job)
    {
        _owner = owner;
        _job = job;
        CancelCommand = new ReactiveCommand()
            .WithSubscribe(() => _owner.CancelJob(_job.JobId));
    }

    public string FileName => Path.GetFileName(_job.Source.AbsolutePath);

    public string Preset => _job.Preset.ToString();

    public string Status => string.IsNullOrWhiteSpace(_job.StatusMessage)
        ? _job.Status.ToString()
        : $"{_job.Status}: {_job.StatusMessage}";

    public double ProgressValue => _job.LatestProgress?.FractionComplete ?? 0;

    public string ProgressText => _job.LatestProgress is { } progress
        ? $"{Math.Clamp(progress.FractionComplete, 0, 1):P0}"
        : string.Empty;

    public ReactiveCommand CancelCommand { get; }
}
