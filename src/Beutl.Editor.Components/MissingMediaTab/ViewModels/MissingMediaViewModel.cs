using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.MissingMediaTab.ViewModels;

public sealed class MissingMediaRowViewModel : IDisposable
{
    public MissingMediaRowViewModel(MissingMedia media)
    {
        Media = media;
        Elements = string.Join(", ", media.References.Select(reference => reference.Element?.Name.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct());
        IsReady = ReplacementPath.CombineLatest(IsOffline, (path, offline) => path != null && !offline)
            .ToReadOnlyReactivePropertySlim();
        HasIssue = Status.Select(status => status == MissingMediaStrings.NoUniqueMatch || status == MissingMediaStrings.Unrecognized)
            .ToReadOnlyReactivePropertySlim();
        HasCandidate = CandidatePath.CombineLatest(IsOffline, (path, offline) => path != null && !offline)
            .ToReadOnlyReactivePropertySlim();
        StateKind = IsOffline.CombineLatest(HasIssue, HasCandidate, IsReady, (offline, issue, candidate, ready) =>
                offline ? StatusLabelKind.Neutral
                : issue ? StatusLabelKind.Critical
                : candidate ? StatusLabelKind.Accent
                : ready ? StatusLabelKind.Success
                : StatusLabelKind.Caution)
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
    public ReadOnlyReactivePropertySlim<StatusLabelKind> StateKind { get; }
    internal IReadOnlyList<string> FontReplacementFiles { get; set; } = [];

    public void Dispose()
    {
        IsReady.Dispose();
        HasIssue.Dispose();
        HasCandidate.Dispose();
        StateText.Dispose();
        StateKind.Dispose();
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
    private readonly IEditorContext _editor;
    private readonly IMediaRepairHost _host;
    private readonly Scene _scene;
    private readonly MissingMediaService _service;
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationToken _token;
    private readonly List<IDisposable> _subscriptions = [];
    private CancellationTokenSource? _candidateCancellation;
    private Task _candidateScanTask = Task.CompletedTask;
    private bool _disposed;

    public MissingMediaViewModel(IEditorContext editor, IReadOnlyList<MissingMedia>? missing = null)
    {
        _editor = editor;
        _host = editor.GetRequiredService<IMediaRepairHost>();
        _service = _host.MissingMediaDetector;
        _scene = editor.GetRequiredService<Scene>();
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(_host.RepairCancellationToken);
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
    // Test seam: tests replace the candidate search; the app leaves it null.
    internal Func<IEnumerable<MissingMedia>, string, CancellationToken,
        Task<IReadOnlyDictionary<MissingMedia, IReadOnlyList<string>>>>? FindCandidateMatches
    { get; set; }
    public Task RefreshAsync() => _host.RefreshMissingMediaAsync();

    // Test seam: tests await the candidate scan.
    internal Task WaitForCandidatesAsync() => _candidateScanTask;

    internal async Task RefreshRowsAsync()
        => RefreshRows(await _host.FindMissingMediaAsync(_token));

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
        foreach (var media in missing ?? _service.FindMissing(_scene))
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
        if (IsEmpty.Value) _host.DismissMissingMediaNotification();
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
        _candidateCancellation?.Cancel();
        SetBusy(true);
        Error.Value = null;
        try
        {
            row.CandidatePath.Value = null;
            await SetReplacementCoreAsync(row, path);
            if (row.ReplacementPath.Value != null)
            {
                // Another kind of the same file, such as the audio of a video, follows the choice. The search below
                // would read the whole file again to identify it.
                foreach (var other in Rows.Where(other => other != row && other.Media.ExpectedUri is { } uri
                             && uri == row.Media.ExpectedUri && !other.IsOffline.Value && other.ReplacementPath.Value == null).ToArray())
                {
                    await SetReplacementCoreAsync(other, path, followsChoice: true);
                }

                string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
                _editor.GetRequiredService<GlobalConfiguration>().EditorConfig.LastMediaDirectory.Value = directory;
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
        if (_disposed || IsBusy.Value || row.IsOffline.Value || row.ReplacementPath.Value != null || row.CandidatePath.Value == null) return;
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
        bool asCandidate = false, CancellationToken? cancellationToken = null, bool followsChoice = false)
    {
        CancellationToken token = cancellationToken ?? _token;
        try
        {
            await _service.ValidateAsync(row.Media, path, token);
            IReadOnlyList<string> fontFiles = [];
            if (row.Media.Kind == MissingMediaKind.Font)
            {
                fontFiles = await _service.SelectFontFamilyFilesAsync(row.Media.FontFamily!, path, matchedPaths ?? [path], token);
            }
            token.ThrowIfCancellationRequested();
            if (asCandidate && (!Rows.Contains(row) || row.IsOffline.Value || row.ReplacementPath.Value != null)) return;
            row.FontReplacementFiles = fontFiles;
            if (asCandidate) row.CandidatePath.Value = Path.GetFullPath(path);
            else
            {
                row.CandidatePath.Value = null;
                row.ReplacementPath.Value = Path.GetFullPath(path);
            }
            row.IsOffline.Value = false;
            row.Status.Value = asCandidate ? MissingMediaStrings.Candidate : MissingMediaStrings.Ready;
        }
        catch (OperationCanceledException) { throw; }
        // The chosen file need not hold this kind of media; the row waits for a file of its own.
        catch (Exception) when (followsChoice) { }
        catch (Exception)
        {
            if (asCandidate && (!Rows.Contains(row) || row.IsOffline.Value
                || row.ReplacementPath.Value != null || row.CandidatePath.Value != null)) return;
            if (!_disposed)
            {
                // Leave the original reference intact. The row can be retried or kept offline.
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
        IDisposable? keepOpen = _editor.GetRequiredService<IToolDockHost>().KeepOpen(this);
        IDisposable? suspension = null;
        Error.Value = null;
        try
        {
            HistoryManager history = _editor.GetRequiredService<HistoryManager>();
            using var write = await _editor.GetRequiredService<IProjectFileWriteAdmission>().BeginProjectFileWriteAsync(_token);
            suspension = _host.SuspendEditor();
            await _editor.GetRequiredService<IPreviewPlayer>().Pause();
            _token.ThrowIfCancellationRequested();
            var selected = Rows.Where(row => !row.IsOffline.Value && row.ReplacementPath.Value != null)
                .Select(row => new MediaRepairSelection(row.Media, row.ReplacementPath.Value!, row.FontReplacementFiles)).ToArray();
            var validated = new List<MissingMediaRepair.Item>();
            // Revalidate after any time spent waiting for file-write admission.
            foreach (var selection in selected)
                validated.Add(await MissingMediaRepair.ValidateAsync(_service, selection, _token));
            _token.ThrowIfCancellationRequested();

            var repair = MissingMediaRepair.Create(_scene, history, _editor.GetRequiredService<FontManager>(),
                _host.ReloadBundledFonts, _host.GetMediaReferencesForRepair(), validated, includeRecoveryProtected: true);
            if (repair is null || repair.Applied.Length == 0)
            {
                await RefreshRowsAsync();
                Error.Value = MissingMediaStrings.SceneChanged;
                return false;
            }

            bool hadRepairs = _host.HasMediaRepairs;
            try
            {
                using (repair)
                {
                    await repair.ApplyAsync(_token);
                    _host.HasMediaRepairs = true;
                    if (!await _host.SaveRepairsAsync())
                        throw new IOException(MessageStrings.UnableToSaveFile);
                    repair.Commit();
                }
            }
            catch
            {
                _host.HasMediaRepairs = hadRepairs;
                _host.ClearRenderCaches();
                throw;
            }
            _host.ClearRenderCaches();
            await RefreshRowsAsync();
            // The repair is on disk, and the snapshot of thousands of changed files can take a while. The write
            // admission keeps other writers out meanwhile, so neither the editor nor this tab has to wait for Git.
            suspension.Dispose();
            suspension = null;
            keepOpen.Dispose();
            keepOpen = null;
            SetBusy(false);
            if (_editor.GetService(typeof(IProjectVersionControlCoordinator)) is IProjectVersionControlSession session)
            {
                try
                {
                    // The tab can close from here on, which must not cancel the snapshot.
                    await session.NotifySavedAsync(write, _host.RepairCancellationToken);
                    _host.ScheduleMediaFingerprints(finishSaveSnapshot: true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The repair has reached disk; a snapshot failure cannot undo that save.
                    s_logger.LogWarning(ex, "Could not snapshot the saved media repair.");
                    if (!_disposed) Error.Value = ex.Message;
                }
            }
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
            suspension?.Dispose();
            // Already let go of, along with the busy state, if the repair got as far as its snapshot.
            if (keepOpen != null)
            {
                keepOpen.Dispose();
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
