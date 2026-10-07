using Beutl.Configuration;
using Beutl.Editor;
using Beutl.IO;
using Beutl.Language;
using Beutl.Media;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed class MissingMediaRowViewModel : IDisposable
{
    public MissingMediaRowViewModel(MissingMedia media)
    {
        Media = media;
        Elements = string.Join(", ", media.References.Select(reference => reference.Element?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct());
    }

    public MissingMedia Media { get; }
    public string Name => Media.Name;
    public string ExpectedLocation => Media.ExpectedUri?.LocalPath ?? Media.FontFamily!.Name;
    public string Elements { get; }
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
    internal IFileSource? ValidatedSource { get; set; }

    public void Dispose()
    {
        ReplacementPath.Dispose();
        Status.Dispose();
        IsOffline.Dispose();
    }
}

public sealed class MissingMediaDialogViewModel : IDisposable
{
    private readonly EditViewModel _editor;
    private readonly MissingMediaService _service = new();
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationToken _token;
    private readonly List<IDisposable> _subscriptions = [];
    private bool _disposed;

    public MissingMediaDialogViewModel(EditViewModel editor)
    {
        _editor = editor;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(editor.MediaRepairCancellationToken);
        _token = _cancellation.Token;
        Rows = _service.FindMissing(editor.Scene).Select(media => new MissingMediaRowViewModel(media)).ToArray();
        SearchDirectory.Value = GlobalConfiguration.Instance.EditorConfig.LastMediaDirectory;
        foreach (var row in Rows)
            _subscriptions.Add(row.IsOffline.Subscribe(_ => RefreshState()));
        RefreshState();
    }

    public IReadOnlyList<MissingMediaRowViewModel> Rows { get; }
    public ReactivePropertySlim<string?> SearchDirectory { get; } = new();
    public ReactivePropertySlim<bool> IsBusy { get; } = new();
    public ReactivePropertySlim<bool> CanEdit { get; } = new(true);
    public ReactivePropertySlim<bool> CanApply { get; } = new();
    public ReactivePropertySlim<bool> CanDismiss { get; } = new(true);
    public ReactivePropertySlim<string> Summary { get; } = new(string.Empty);
    public ReactivePropertySlim<string?> Error { get; } = new();
    public bool IsApplying { get; private set; }
    public bool CanClose => !IsApplying || _token.IsCancellationRequested;

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
                if (matches.TryGetValue(row.Media, out string? path))
                    await SetReplacementCoreAsync(row, path);
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

    private async Task SetReplacementCoreAsync(MissingMediaRowViewModel row, string path)
    {
        try
        {
            var validated = await _service.ValidateAsync(row.Media, path, _token);
            _token.ThrowIfCancellationRequested();
            row.ValidatedSource = validated;
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
        IsApplying = true;
        CanDismiss.Value = false;
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
                // A repair keeps saved child meshes, including their order and edits.
                // Replace geometry only when a saved hash proves this is a different asset.
                if (row.Media.Kind == MissingMediaKind.Model && row.Media.Fingerprint is { } fingerprint
                    && !string.Equals(fingerprint.Sha256,
                        await MissingMediaService.HashFileAsync(row.ReplacementPath.Value!, _token), StringComparison.OrdinalIgnoreCase))
                    replacementModels.Add(row);
            }
            _token.ThrowIfCancellationRequested();
            using (_editor.HistoryManager.SuppressRecording())
            {
                foreach (var row in rows.Where(row => row.Media.Kind != MissingMediaKind.Font))
                {
                    var uri = new Uri(row.ReplacementPath.Value!);
                    foreach (IFileSource source in row.Media.References.Select(reference => reference.Source)
                                 .OfType<IFileSource>().Distinct<IFileSource>(ReferenceEqualityComparer.Instance))
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
                var paths = fontRows.ToDictionary(row => row.Media.FontFamily!.Name, row => row.ReplacementPath.Value!);
                var relocation = new ResourceRelocationService(family => [paths[family]]);
                RelocationResult result = await relocation.RelocateFontsAsync(fontRows.Select(row => row.Media.FontFamily!),
                    projectDirectory, _token);
                if (result.FailedResources.Count > 0) throw new IOException(MissingMediaStrings.FontCopyFailed);
                var project = _editor.Scene.FindHierarchicalParent<Project>() ?? new Project { Uri = _editor.Scene.Uri };
                FontManager.Instance.LoadProjectFonts(project);
            }

            await _editor.SaveAsync();
            if (_editor.EditorService.ProjectVersionControlSession is { } session)
                await session.NotifySavedAsync(write, _token);
            _editor.Renderer.Value.ClearAllCaches();
            _editor.FrameCacheManager.Value.Clear();
            _editor.Player.QueuePreviewRender();
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
            IsApplying = false;
            if (!_disposed)
            {
                CanDismiss.Value = true;
                SetBusy(false);
            }
        }
    }

    private void SetBusy(bool busy)
    {
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
        CanDismiss.Dispose();
        Summary.Dispose();
        Error.Dispose();
    }
}
