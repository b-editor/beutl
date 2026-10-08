using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.IO;
using Beutl.Language;
using Beutl.Media;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.Logging;
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
        HasIssue = Status.Select(status => status == MissingMediaStrings.NoUniqueMatch || status == MissingMediaStrings.Unrecognized)
            .ToReadOnlyReactivePropertySlim();
        HasCandidate = CandidatePath.CombineLatest(IsOffline, (path, offline) => path != null && !offline)
            .ToReadOnlyReactivePropertySlim();
        StateText = Status.CombineLatest(IsOffline, (status, offline) => (offline ? MissingMediaStrings.Offline
                : status == MissingMediaStrings.Missing || status == MissingMediaStrings.Ready || status == MissingMediaStrings.Candidate
                    ? status : MissingMediaStrings.NeedsAttention) ?? string.Empty)
            .ToReadOnlyReactivePropertySlim();
    }

    public MissingMedia Media { get; internal set; }
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
    public ReactivePropertySlim<string?> CandidatePath { get; } = new();
    public ReactivePropertySlim<string> Status { get; } = new(MissingMediaStrings.Missing);
    public ReactivePropertySlim<bool> IsOffline { get; } = new();
    public ReactivePropertySlim<bool> IsExpanded { get; } = new();
    public ReadOnlyReactivePropertySlim<bool> IsReady { get; }
    public ReadOnlyReactivePropertySlim<bool> HasIssue { get; }
    public ReadOnlyReactivePropertySlim<bool> HasCandidate { get; }
    public ReadOnlyReactivePropertySlim<string?> StateText { get; }
    internal IFileSource? ValidatedSource { get; set; }
    internal IReadOnlyList<string> FontReplacementFiles { get; set; } = [];

    public void Dispose()
    {
        IsReady.Dispose();
        HasIssue.Dispose();
        HasCandidate.Dispose();
        StateText.Dispose();
        IsExpanded.Dispose();
        ReplacementPath.Dispose();
        CandidatePath.Dispose();
        Status.Dispose();
        IsOffline.Dispose();
    }
}

public sealed class MissingMediaViewModel : IToolContext
{
    private static readonly ILogger s_logger = Beutl.Logging.Log.CreateLogger<MissingMediaViewModel>();
    private readonly EditViewModel _editor;
    private readonly MissingMediaService _service = new();
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationToken _token;
    private readonly List<IDisposable> _subscriptions = [];
    private CancellationTokenSource? _candidateCancellation;
    private Task _candidateScanTask = Task.CompletedTask;
    private bool _disposed;

    public MissingMediaViewModel(EditViewModel editor, IReadOnlyList<MissingMedia>? missing = null)
    {
        _editor = editor;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(editor.MediaRepairCancellationToken);
        _token = _cancellation.Token;
        RefreshRows(missing);
    }

    public ToolTabExtension Extension => MissingMediaTabExtension.Instance;
    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();
    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(MissingMediaStrings.Title);
    public ObservableCollection<MissingMediaRowViewModel> Rows { get; } = [];
    public ReactivePropertySlim<bool> IsEmpty { get; } = new();
    public ReactivePropertySlim<bool> IsBusy { get; } = new();
    public ReactivePropertySlim<bool> CanEdit { get; } = new(true);
    public ReactivePropertySlim<bool> CanApply { get; } = new();
    public ReactivePropertySlim<bool> CanUseCandidates { get; } = new();
    public ReactivePropertySlim<string> Summary { get; } = new(string.Empty);
    public ReactivePropertySlim<string?> Error { get; } = new();
    internal CancellationToken CancellationToken => _token;
    internal Func<IEnumerable<MissingMedia>, string, CancellationToken,
        Task<IReadOnlyDictionary<MissingMedia, IReadOnlyList<string>>>>? FindCandidateMatches
    { get; set; }
    public Task RefreshAsync() => _editor.OpenMissingMediaAsync(refresh: true);

    public void Close() => _editor.CloseToolTab(this);

    internal Task WaitForCandidatesAsync() => _candidateScanTask;

    internal async Task RefreshRowsAsync()
        => RefreshRows(await _service.FindMissingAsync(_editor.Scene, _token));

    internal async Task InitializeRowsAsync()
    {
        SetBusy(true);
        IsEmpty.Value = false;
        try { await RefreshRowsAsync(); }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed) Error.Value = ex.Message; }
        finally { if (!_disposed) SetBusy(false); }
    }

    internal void RefreshRows(IReadOnlyList<MissingMedia>? missing = null)
    {
        if (_disposed) return;
        var previous = Rows.ToDictionary(row => (row.Media.Kind, row.Media.ExpectedUri, row.Media.FontFamily?.Name));
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();
        var retained = new List<MissingMediaRowViewModel>();
        foreach (var media in missing ?? _service.FindMissing(_editor.Scene))
        {
            var key = (media.Kind, media.ExpectedUri, media.FontFamily?.Name);
            var row = previous.TryGetValue(key, out var existing) ? existing : new MissingMediaRowViewModel(media);
            row.Media = media;
            retained.Add(row);
            _subscriptions.Add(row.IsOffline.Subscribe(_ => RefreshState()));
        }
        foreach (var row in Rows.Where(row => !retained.Contains(row)).ToArray())
        {
            Rows.Remove(row);
            row.Dispose();
        }
        for (int i = 0; i < retained.Count; i++)
        {
            int index = Rows.IndexOf(retained[i]);
            if (index < 0) Rows.Insert(i, retained[i]);
            else if (index != i) Rows.Move(index, i);
        }
        IsEmpty.Value = Rows.Count == 0;
        if (IsEmpty.Value) _editor.DismissMissingMediaNotification();
        RefreshState();
    }

    public object? GetService(Type serviceType) => _editor.GetService(serviceType);

    public void ReadFromJson(JsonObject json) { }

    public void WriteToJson(JsonObject json) { }

    private async Task FindCandidatesInDirectoryAsync(string directory, MissingMediaRowViewModel selectedRow, CancellationToken token)
    {
        // Let the selected replacement become usable before unrelated discovery.
        await Task.Yield();
        try
        {
            var rows = Rows.Where(row => row != selectedRow && !row.IsOffline.Value && row.ReplacementPath.Value == null).ToArray();
            if (selectedRow.Media.Kind == MissingMediaKind.Font)
            {
                string? selected = selectedRow.ReplacementPath.Value;
                if (selected != null)
                {
                    var fonts = await _service.FindFontFamilyFilesAsync(selectedRow.Media.FontFamily!, directory, token, selected);
                    if (Rows.Contains(selectedRow) && selectedRow.ReplacementPath.Value == selected)
                        selectedRow.FontReplacementFiles = fonts;
                }
            }
            if (rows.Length == 0) return;
            var matches = await (FindCandidateMatches?.Invoke(rows.Select(row => row.Media), directory, token)
                ?? _service.FindMatchesAsync(rows.Select(row => row.Media), directory, token));
            foreach (var row in rows)
            {
                token.ThrowIfCancellationRequested();
                if (!Rows.Contains(row) || row.IsOffline.Value || row.ReplacementPath.Value != null) continue;
                if (matches.TryGetValue(row.Media, out var paths))
                    await SetReplacementCoreAsync(row, paths[0], paths, asCandidate: true, cancellationToken: token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed) Error.Value = ex.Message; }
        finally { if (!_disposed) RefreshState(); }
    }

    public async Task SetReplacementAsync(MissingMediaRowViewModel row, string path)
    {
        if (_disposed || IsBusy.Value) return;
        SetBusy(true);
        Error.Value = null;
        try
        {
            row.CandidatePath.Value = null;
            await SetReplacementCoreAsync(row, path);
            if (row.ReplacementPath.Value != null)
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
                GlobalConfiguration.Instance.EditorConfig.LastMediaDirectory = directory;
                _candidateCancellation?.Cancel();
                _candidateCancellation?.Dispose();
                _candidateCancellation = CancellationTokenSource.CreateLinkedTokenSource(_token);
                _candidateScanTask = FindCandidatesInDirectoryAsync(directory, row, _candidateCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed) Error.Value = ex.Message; }
        finally { if (!_disposed) SetBusy(false); }
    }

    public void UseCandidate(MissingMediaRowViewModel row)
    {
        if (_disposed || IsBusy.Value || row.IsOffline.Value || row.CandidatePath.Value == null) return;
        row.ReplacementPath.Value = row.CandidatePath.Value;
        row.CandidatePath.Value = null;
        row.Status.Value = MissingMediaStrings.Ready;
        RefreshState();
    }

    public void UseAllCandidates()
    {
        if (_disposed || IsBusy.Value) return;
        foreach (var row in Rows) UseCandidate(row);
    }

    private async Task SetReplacementCoreAsync(MissingMediaRowViewModel row, string path, IReadOnlyList<string>? matchedPaths = null,
        bool asCandidate = false, CancellationToken? cancellationToken = null)
    {
        CancellationToken token = cancellationToken ?? _token;
        try
        {
            var validated = await _service.ValidateAsync(row.Media, path, token);
            IReadOnlyList<string> fontFiles = [];
            if (row.Media.Kind == MissingMediaKind.Font)
            {
                fontFiles = await _service.SelectFontFamilyFilesAsync(row.Media.FontFamily!, path, matchedPaths ?? [path], token);
            }
            token.ThrowIfCancellationRequested();
            if (asCandidate && (!Rows.Contains(row) || row.IsOffline.Value || row.ReplacementPath.Value != null)) return;
            row.ValidatedSource = validated;
            row.FontReplacementFiles = fontFiles;
            if (asCandidate) row.CandidatePath.Value = Path.GetFullPath(path);
            else row.ReplacementPath.Value = Path.GetFullPath(path);
            row.IsOffline.Value = false;
            row.Status.Value = asCandidate ? MissingMediaStrings.Candidate : MissingMediaStrings.Ready;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            if (asCandidate && (!Rows.Contains(row) || row.IsOffline.Value
                || row.ReplacementPath.Value != null || row.CandidatePath.Value != null)) return;
            if (!_disposed)
            {
                // Leave the original reference intact. The row can be retried or kept offline.
                row.ValidatedSource = null;
                row.FontReplacementFiles = [];
                row.ReplacementPath.Value = null;
                row.CandidatePath.Value = null;
                row.Status.Value = MissingMediaStrings.Unrecognized;
                Error.Value = $"{row.Name}: {MissingMediaStrings.Unrecognized}";
            }
        }
    }

    public async Task<bool> ApplyAsync()
    {
        if (_disposed || !CanApply.Value) return false;
        _candidateCancellation?.Cancel();
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
                .ToLookup(source => (MissingMediaService.GetKind(source), source.Uri));
            string projectDirectory = Path.GetDirectoryName((_editor.Scene.FindHierarchicalParent<Project>()?.Uri
                                      ?? _editor.Scene.Uri)!.LocalPath)!;
            string fontsDirectory = Path.Combine(projectDirectory, "resources", "fonts");
            var originalFonts = Directory.Exists(fontsDirectory)
                ? Directory.GetFiles(fontsDirectory).ToHashSet(StringComparer.Ordinal) : [];
            var fingerprints = new Dictionary<string, Beutl.ProjectSystem.MediaFileFingerprint>(_editor.Scene.MediaFingerprints);
            bool hadRepairs = _editor.HasMediaRepairs.Value;
            var rollback = new List<Action>();
            foreach (var source in rows.Where(row => row.Media.Kind != MissingMediaKind.Font)
                         .SelectMany(row => currentSources[(row.Media.Kind, row.Media.ExpectedUri!)])
                         .Distinct<IFileSource>(ReferenceEqualityComparer.Instance))
            {
                Uri original = source.Uri;
                if (source is Beutl.Graphics3D.Models.ModelSource model)
                {
                    var snapshot = model.CaptureState();
                    rollback.Add(() => model.RelinkFrom(snapshot));
                    if (model.FindHierarchicalParent<Beutl.Graphics3D.Models.Model3D>() is { } owner)
                        rollback.Add(owner.CaptureGeometryRollback());
                }
                else rollback.Add(() => source.ReadFrom(original));
            }
            var fontRows = rows.Where(row => row.Media.Kind == MissingMediaKind.Font).ToArray();
            try
            {
                // Copy and validate fonts before mutating any live media reference.
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
                using (_editor.HistoryManager.SuppressRecording())
                {
                    foreach (var row in rows.Where(row => row.Media.Kind != MissingMediaKind.Font))
                    {
                        var uri = new Uri(row.ReplacementPath.Value!);
                        foreach (IFileSource source in currentSources[(row.Media.Kind, row.Media.ExpectedUri!)])
                            ResourceRelocationService.RelinkFileSource(source, uri, row.ValidatedSource,
                                synchronizeModelGeometry: replacementModels.Contains(row));
                    }
                }
                _editor.HasMediaRepairs.Value = true;

                if (!await _editor.SaveAsync()) throw new IOException(MessageStrings.UnableToSaveFile);
            }
            catch
            {
                using (_editor.HistoryManager.SuppressRecording())
                    foreach (var restore in rollback) restore();
                _editor.Scene.MediaFingerprints.Clear();
                foreach (var pair in fingerprints) _editor.Scene.MediaFingerprints[pair.Key] = pair.Value;
                _editor.HasMediaRepairs.Value = hadRepairs;
                if (fontRows.Length > 0)
                {
                    try
                    {
                        if (Directory.Exists(fontsDirectory))
                            foreach (string file in Directory.GetFiles(fontsDirectory).Where(file => !originalFonts.Contains(file)))
                                File.Delete(file);
                        FontManager.Instance.LoadProjectFonts(_editor.Scene.FindHierarchicalParent<Project>() ?? new Project { Uri = _editor.Scene.Uri });
                    }
                    catch (Exception ex) { s_logger.LogWarning(ex, "Could not roll back copied repair fonts."); }
                }
                _editor.Renderer.Value.ClearAllCaches();
                _editor.FrameCacheManager.Value.Clear();
                _editor.Player.QueuePreviewRender();
                throw;
            }
            if (_editor.EditorService.ProjectVersionControlSession is { } session)
            {
                try
                {
                    await session.NotifySavedAsync(write, _token);
                    _editor.ScheduleMediaFingerprints(finishSaveSnapshot: true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The repair has reached disk; a snapshot failure cannot undo that save.
                    s_logger.LogWarning(ex, "Could not snapshot the saved media repair.");
                    Error.Value = ex.Message;
                }
            }
            _editor.Renderer.Value.ClearAllCaches();
            _editor.FrameCacheManager.Value.Clear();
            _editor.Player.QueuePreviewRender();
            await RefreshRowsAsync();
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
        int candidates = Rows.Count(row => !row.IsOffline.Value && row.CandidatePath.Value != null);
        Summary.Value = string.Format(MissingMediaStrings.CandidateSummary, Rows.Count, ready, candidates, offline);
        CanApply.Value = !IsBusy.Value && ready > 0;
        CanUseCandidates.Value = !IsBusy.Value && candidates > 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
        _candidateCancellation?.Cancel();
        _candidateCancellation?.Dispose();
        foreach (var subscription in _subscriptions) subscription.Dispose();
        foreach (var row in Rows) row.Dispose();
        IsBusy.Dispose();
        CanEdit.Dispose();
        CanApply.Dispose();
        CanUseCandidates.Dispose();
        IsSelected.Dispose();
        Header.Dispose();
        IsEmpty.Dispose();
        Summary.Dispose();
        Error.Dispose();
    }
}
