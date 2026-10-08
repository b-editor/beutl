using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.IO;
using Beutl.Language;
using Beutl.Media;
using Beutl.Services.PrimitiveImpls;
using Reactive.Bindings;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.ViewModels.Tools;

public sealed class MissingMediaRowViewModel : IDisposable
{
    public MissingMediaRowViewModel(MissingMedia media)
    {
        Media = media;
        Elements = string.Join(", ", media.References.Select(reference => reference.Element?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct());
        IsReady = ReplacementPath.CombineLatest(IsOffline, (path, offline) => path != null && !offline)
            .ToReadOnlyReactivePropertySlim();
        HasIssue = Status.Select(status => status != MissingMediaStrings.Missing && status != MissingMediaStrings.Ready)
            .ToReadOnlyReactivePropertySlim();
        StateText = Status.CombineLatest(IsOffline, (status, offline) => (offline ? MissingMediaStrings.Offline
                : status == MissingMediaStrings.Missing || status == MissingMediaStrings.Ready ? status : MissingMediaStrings.NeedsAttention) ?? string.Empty)
            .ToReadOnlyReactivePropertySlim();
    }

    public MissingMedia Media { get; }
    public string Name => Media.Name;
    public string ExpectedLocation => Media.ExpectedUri?.LocalPath ?? Media.FontFamily!.Name;
    public string Elements { get; }
    public Icon MediaIcon => Media.Kind switch
    {
        MissingMediaKind.Video => Icon.Video,
        MissingMediaKind.Sound => Icon.MusicNote1,
        MissingMediaKind.Image => Icon.Image,
        MissingMediaKind.Cube => Icon.Color,
        MissingMediaKind.Font => Icon.TextFont,
        _ => Icon.Document
    };
    public string Kind => Media.Kind switch
    {
        MissingMediaKind.Video => MissingMediaStrings.Video,
        MissingMediaKind.Sound => MissingMediaStrings.Sound,
        MissingMediaKind.Image => MissingMediaStrings.Image,
        MissingMediaKind.Model => MissingMediaStrings.Model,
        MissingMediaKind.Cube => MissingMediaStrings.Cube,
        _ => MissingMediaStrings.Font
    };

    public ReactivePropertySlim<string?> ReplacementPath { get; } = new();
    public ReactivePropertySlim<string> Status { get; } = new(MissingMediaStrings.Missing);
    public ReactivePropertySlim<bool> IsOffline { get; } = new();
    public ReactivePropertySlim<bool> IsExpanded { get; } = new();
    public ReadOnlyReactivePropertySlim<bool> IsReady { get; }
    public ReadOnlyReactivePropertySlim<bool> HasIssue { get; }
    public ReadOnlyReactivePropertySlim<string?> StateText { get; }
    internal IFileSource? ValidatedSource { get; set; }
    internal IReadOnlyList<string> FontReplacementFiles { get; set; } = [];

    public void Dispose()
    {
        IsReady.Dispose();
        HasIssue.Dispose();
        StateText.Dispose();
        IsExpanded.Dispose();
        ReplacementPath.Dispose();
        Status.Dispose();
        IsOffline.Dispose();
    }
}

public sealed class MissingMediaViewModel : IToolContext
{
    private readonly EditViewModel _editor;
    private readonly MissingMediaService _service = new();
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationToken _token;
    private readonly List<IDisposable> _subscriptions = [];
    private bool _disposed;

    public MissingMediaViewModel(EditViewModel editor)
    {
        _editor = editor;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(editor.MediaRepairCancellationToken);
        _token = _cancellation.Token;
        SearchDirectory.Value = GlobalConfiguration.Instance.EditorConfig.LastMediaDirectory;
        RefreshRows();
    }

    public ToolTabExtension Extension => MissingMediaTabExtension.Instance;
    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();
    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(MissingMediaStrings.Title);
    public ObservableCollection<MissingMediaRowViewModel> Rows { get; } = [];
    public ReactivePropertySlim<bool> IsEmpty { get; } = new();
    public ReactivePropertySlim<string?> SearchDirectory { get; } = new();
    public ReactivePropertySlim<bool> IsBusy { get; } = new();
    public ReactivePropertySlim<bool> CanEdit { get; } = new(true);
    public ReactivePropertySlim<bool> CanApply { get; } = new();
    public ReactivePropertySlim<string> Summary { get; } = new(string.Empty);
    public ReactivePropertySlim<string?> Error { get; } = new();
    internal CancellationToken CancellationToken => _token;
    public Task RefreshAsync() => _editor.OpenMissingMediaAsync(refresh: true);

    public void Close() => _editor.CloseToolTab(this);

    internal void RefreshRows()
    {
        var offline = Rows.Where(row => row.IsOffline.Value)
            .Select(row => (row.Media.Kind, row.Media.ExpectedUri, row.Media.FontFamily?.Name)).ToHashSet();
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();
        foreach (var row in Rows) row.Dispose();
        Rows.Clear();
        foreach (var media in _service.FindMissing(_editor.Scene))
        {
            var row = new MissingMediaRowViewModel(media);
            row.IsOffline.Value = offline.Contains((media.Kind, media.ExpectedUri, media.FontFamily?.Name));
            Rows.Add(row);
            _subscriptions.Add(row.IsOffline.Subscribe(_ => RefreshState()));
        }
        IsEmpty.Value = Rows.Count == 0;
        if (IsEmpty.Value) _editor.DismissMissingMediaNotification();
        RefreshState();
    }

    public object? GetService(Type serviceType) => _editor.GetService(serviceType);

    public void ReadFromJson(JsonObject json) { }

    public void WriteToJson(JsonObject json) { }

    public async Task FindInDirectoryAsync(string directory, MissingMediaRowViewModel? origin = null)
    {
        if (_disposed || IsBusy.Value) return;
        SetBusy(true);
        Error.Value = null;
        try
        {
            SearchDirectory.Value = directory;
            GlobalConfiguration.Instance.EditorConfig.LastMediaDirectory = directory;
            var rows = Rows.Where(row => !row.IsOffline.Value && row.ReplacementPath.Value == null
                && (origin == null || row.Media.ExpectedUri != null && origin.Media.ExpectedUri != null
                    && Path.GetDirectoryName(row.ExpectedLocation) == Path.GetDirectoryName(origin.ExpectedLocation)
                    || ReferenceEquals(row, origin))).ToArray();
            var matches = await _service.FindMatchesAsync(rows.Select(row => row.Media), directory, _token);
            foreach (var row in rows)
            {
                _token.ThrowIfCancellationRequested();
                if (matches.TryGetValue(row.Media, out var paths))
                    await SetReplacementCoreAsync(row, paths[0], paths);
                else row.Status.Value = MissingMediaStrings.NoUniqueMatch;
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed) Error.Value = ex.Message; }
        finally { if (!_disposed) SetBusy(false); }
    }

    public async Task SetReplacementAsync(MissingMediaRowViewModel row, string path)
    {
        if (_disposed || IsBusy.Value) return;
        SetBusy(true);
        Error.Value = null;
        try
        {
            await SetReplacementCoreAsync(row, path);
            if (row.ReplacementPath.Value != null)
                GlobalConfiguration.Instance.EditorConfig.LastMediaDirectory = Path.GetDirectoryName(path);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        finally { if (!_disposed) SetBusy(false); }
    }

    private async Task SetReplacementCoreAsync(MissingMediaRowViewModel row, string path, IReadOnlyList<string>? matchedPaths = null)
    {
        try
        {
            var validated = await _service.ValidateAsync(row.Media, path, _token);
            IReadOnlyList<string> fontFiles = [];
            if (row.Media.Kind == MissingMediaKind.Font)
            {
                fontFiles = matchedPaths ?? await _service.FindFontFamilyFilesAsync(row.Media.FontFamily!, Path.GetDirectoryName(Path.GetFullPath(path))!, _token);
                foreach (string file in fontFiles) await _service.ValidateAsync(row.Media, file, _token);
            }
            _token.ThrowIfCancellationRequested();
            row.ValidatedSource = validated;
            row.FontReplacementFiles = fontFiles;
            row.ReplacementPath.Value = Path.GetFullPath(path);
            row.IsOffline.Value = false;
            row.Status.Value = MissingMediaStrings.Ready;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            if (!_disposed)
            {
                // Leave the original reference intact. The row can be retried or kept offline.
                row.ValidatedSource = null;
                row.FontReplacementFiles = [];
                row.ReplacementPath.Value = null;
                row.Status.Value = MissingMediaStrings.Unrecognized;
                Error.Value = $"{row.Name}: {MissingMediaStrings.Unrecognized}";
            }
        }
    }

    public async Task<bool> ApplyAsync()
    {
        if (_disposed || !CanApply.Value) return false;
        SetBusy(true);
        var dockable = _editor.DockHost.Factory.EnumerateTools().FirstOrDefault(tool => tool.ToolContext == this);
        if (dockable != null) dockable.CanClose = false;
        Error.Value = null;
        try
        {
            using var write = await _editor.EditorService.BeginProjectFileWriteAsync(_token);
            using var suspension = _editor.EditorService.SuspendEditor(_editor);
            await _editor.Player.Pause();
            _token.ThrowIfCancellationRequested();
            var rows = Rows.Where(row => !row.IsOffline.Value && row.ReplacementPath.Value != null).ToArray();
            var replacementModels = new HashSet<MissingMediaRowViewModel>();
            // Revalidate after any time spent waiting for file-write admission.
            foreach (var row in rows)
            {
                row.ValidatedSource = await _service.ValidateAsync(row.Media, row.ReplacementPath.Value!, _token);
                foreach (string path in row.FontReplacementFiles) await _service.ValidateAsync(row.Media, path, _token);
                // A repair keeps saved child meshes, including their order and edits.
                // Replace geometry only when a saved hash proves this is a different asset.
                if (row.Media.Kind == MissingMediaKind.Model && row.Media.Fingerprint is not null
                    && !await MissingMediaService.MatchesFingerprintAsync(row.Media, row.ReplacementPath.Value!,
                        row.ValidatedSource as Beutl.Graphics3D.Models.ModelSource, _token))
                    replacementModels.Add(row);
            }
            _token.ThrowIfCancellationRequested();
            // The scene remains editable while this tab is open; resolve its current
            // source instances rather than the detection-time references.
            var currentSources = new ObjectSearcher(_editor.Scene,
                    value => value is Beutl.Media.Source.MediaSource { HasUri: true }
                        or Beutl.Graphics3D.Models.ModelSource { HasUri: true })
                .SearchAll().OfType<IFileSource>().Distinct<IFileSource>(ReferenceEqualityComparer.Instance)
                .ToLookup(source => (source.GetType(), source.Uri));
            using (_editor.HistoryManager.SuppressRecording())
            {
                foreach (var row in rows.Where(row => row.Media.Kind != MissingMediaKind.Font))
                {
                    var uri = new Uri(row.ReplacementPath.Value!);
                    Type sourceType = row.Media.References.First(reference => reference.Source != null).Source!.GetType();
                    foreach (IFileSource source in currentSources[(sourceType, row.Media.ExpectedUri!)])
                        ResourceRelocationService.RelinkFileSource(source, uri, row.ValidatedSource,
                            synchronizeModelGeometry: replacementModels.Contains(row));
                }
            }
            _editor.HasMediaRepairs.Value = true;

            string projectDirectory = Path.GetDirectoryName((_editor.Scene.FindHierarchicalParent<Project>()?.Uri
                                      ?? _editor.Scene.Uri)!.LocalPath)!;
            var fontRows = rows.Where(row => row.Media.Kind == MissingMediaKind.Font).ToArray();
            if (fontRows.Length > 0)
            {
                var paths = fontRows.ToDictionary(row => row.Media.FontFamily!.Name, row => row.FontReplacementFiles);
                var relocation = new ResourceRelocationService(family => paths[family]);
                RelocationResult result = await relocation.RelocateFontsAsync(fontRows.Select(row => row.Media.FontFamily!),
                    projectDirectory, _token);
                if (result.FailedResources.Count > 0) throw new IOException(MissingMediaStrings.FontCopyFailed);
                var project = _editor.Scene.FindHierarchicalParent<Project>() ?? new Project { Uri = _editor.Scene.Uri };
                FontManager.Instance.LoadProjectFonts(project);
            }

            await _editor.SaveAsync();
            if (_editor.EditorService.ProjectVersionControlSession is { } session)
            {
                await session.NotifySavedAsync(write, _token);
                _editor.ScheduleMediaFingerprints(finishSaveSnapshot: true);
            }
            _editor.Renderer.Value.ClearAllCaches();
            _editor.FrameCacheManager.Value.Clear();
            _editor.Player.QueuePreviewRender();
            RefreshRows();
            return true;
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            if (!_disposed) Error.Value = ex.Message;
            return false;
        }
        finally
        {
            if (dockable != null) dockable.CanClose = true;
            if (!_disposed)
            {
                SetBusy(false);
            }
        }
    }

    internal void SetBusy(bool busy)
    {
        if (_disposed) return;
        IsBusy.Value = busy;
        CanEdit.Value = !busy;
        RefreshState();
    }

    private void RefreshState()
    {
        if (_disposed) return;
        int ready = Rows.Count(row => !row.IsOffline.Value && row.ReplacementPath.Value != null);
        int offline = Rows.Count(row => row.IsOffline.Value);
        Summary.Value = string.Format(MissingMediaStrings.Summary, Rows.Count, ready, offline);
        CanApply.Value = !IsBusy.Value && ready > 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
        foreach (var subscription in _subscriptions) subscription.Dispose();
        foreach (var row in Rows) row.Dispose();
        SearchDirectory.Dispose();
        IsBusy.Dispose();
        CanEdit.Dispose();
        CanApply.Dispose();
        IsSelected.Dispose();
        Header.Dispose();
        IsEmpty.Dispose();
        Summary.Dispose();
        Error.Dispose();
    }
}
