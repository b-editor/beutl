using System.ComponentModel;
using Beutl.Editor;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Editor.Components.FileBrowserTab.ViewModels;

internal sealed class ProjectAssetsViewModel : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<ProjectAssetsViewModel>();
    private readonly IProjectAssetHost _host;
    private readonly string _projectDirectory;
    private readonly IDisposable _modeSubscription;
    private CancellationTokenSource? _operation;
    private ProjectAssetPreview? _preview;

    public ProjectAssetsViewModel(IProjectAssetHost host, Project project)
    {
        _host = host;
        Project = project;
        _projectDirectory = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        _modeSubscription = Mode.Subscribe(_ =>
        {
            if (!IsBusy.Value) ClearFeedback();
            UpdateRows();
        });
    }

    internal Project Project { get; }
    public string[] Modes { get; } = [ProjectAssetsStrings.Collect, ProjectAssetsStrings.Unused, ProjectAssetsStrings.Quarantined];
    public ReactivePropertySlim<int> Mode { get; } = new();
    public ReactivePropertySlim<ProjectAssetRow[]> Rows { get; } = new([]);
    public ReactivePropertySlim<bool> IsBusy { get; } = new();
    public ReactivePropertySlim<bool> CanEdit { get; } = new(true);
    public ReactivePropertySlim<bool> CanApply { get; } = new();
    public ReactivePropertySlim<bool> CanUseSecondary { get; } = new();
    public ReactivePropertySlim<bool> CanSelect { get; } = new();
    public ReactivePropertySlim<bool?> AllSelected { get; } = new(false);
    public ReactivePropertySlim<bool> IsEmpty { get; } = new();
    public ReactivePropertySlim<string> ApplyLabel { get; } = new("");
    public ReactivePropertySlim<string?> SecondaryLabel { get; } = new();
    public ReactivePropertySlim<string> SelectionText { get; } = new("");
    public ReactivePropertySlim<string?> Warning { get; } = new();
    public ReactivePropertySlim<string?> Error { get; } = new();
    public ReactivePropertySlim<string?> ErrorDetails { get; } = new();
    public ReactivePropertySlim<string?> Status { get; } = new();
    public ReactivePropertySlim<string?> DeleteConfirmation { get; } = new();

    private ProjectAssetAction Action => Mode.Value switch
    {
        1 => ProjectAssetAction.Quarantine,
        2 => ProjectAssetAction.Restore,
        _ => ProjectAssetAction.Collect,
    };

    internal Task RefreshAsync() => RunAsync(ProjectAssetsStrings.Scanning, LoadAsync);

    internal Task ApplyAsync(bool delete = false)
    {
        if (_preview is not { } preview || !CanApply.Value || delete != (DeleteConfirmation.Value != null))
            return Task.CompletedTask;

        var action = delete ? ProjectAssetAction.Delete : Action;
        ProjectAssetItem[] selected = [.. Rows.Value.Where(row => row.IsSelected && row.Item.CanApply(action)).Select(row => row.Item)];
        DeleteConfirmation.Value = null;
        return RunAsync(ProjectAssetsStrings.Working, async token =>
        {
            try
            {
                await _host.ApplyAsync(Project, preview, action, selected, token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException or ProjectAssetDeleteCanceledException { DeletedCount: > 0 })
            {
                _preview = null;
                UpdateRows();
                try { await LoadAsync(CancellationToken.None); }
                catch (Exception recoveryError)
                {
                    s_logger.LogWarning(recoveryError, "Could not refresh project assets after an operation stopped.");
                }
                throw;
            }

            await LoadAsync(CancellationToken.None);
            Status.Value = ProjectAssetsStrings.Finished;
        });
    }

    internal void ToggleSelection()
    {
        var selectable = Rows.Value.Where(row => row.CanSelect).ToArray();
        bool select = selectable.Any(row => !row.IsSelected);
        foreach (ProjectAssetRow row in selectable) row.SetSelected(select);
        UpdateSelection();
    }

    internal void BeginDelete()
    {
        if (!CanUseSecondary.Value || Action != ProjectAssetAction.Restore || DeleteConfirmation.Value != null) return;
        DeleteConfirmation.Value = string.Format(ProjectAssetsStrings.DeleteConfirmation,
            Rows.Value.Count(row => row.IsSelected && row.Item.CanApply(ProjectAssetAction.Delete)));
        UpdateSelection();
    }

    internal void CancelDelete()
    {
        DeleteConfirmation.Value = null;
        UpdateSelection();
    }

    internal void Cancel() => _operation?.Cancel();

    private async Task LoadAsync(CancellationToken token)
    {
        _preview = await _host.PreviewAsync(Project, token);
        UpdateRows();
    }

    private async Task RunAsync(string status, Func<CancellationToken, Task> operation)
    {
        if (_operation != null) return;
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        ClearFeedback();
        Status.Value = status;
        IsBusy.Value = true;
        UpdateSelection();
        try
        {
            await operation(cancellation.Token);
            if (Status.Value == status) Status.Value = null;
        }
        catch (ProjectAssetDeleteCanceledException ex) when (cancellation.IsCancellationRequested)
        {
            Status.Value = string.Format(ProjectAssetsStrings.DeleteCanceled, ex.DeletedCount);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status.Value = ProjectAssetsStrings.Canceled;
        }
        catch (ProjectAssetPlanChangedException)
        {
            Status.Value = null;
            Error.Value = ProjectAssetsStrings.PlanChanged;
        }
        catch (Exception ex)
        {
            Status.Value = null;
            Error.Value = ex is ProjectAssetValidationException validation
                ? string.Format(ProjectAssetsStrings.ValidationFailed, Path.GetFileName(validation.Path))
                : MessageStrings.OperationFailed;
            ErrorDetails.Value = ex.Message;
        }
        finally
        {
            _operation = null;
            IsBusy.Value = false;
            UpdateSelection();
        }
    }

    private void ClearFeedback()
    {
        Error.Value = null;
        ErrorDetails.Value = null;
        Status.Value = null;
    }

    private void UpdateRows()
    {
        ProjectAssetAction action = Action;
        ProjectAssetIssue? warning = action == ProjectAssetAction.Collect ? null : _preview?.CleanupIssue;
        Warning.Value = DescribeIssue(warning);
        Rows.Value = [.. (_preview?.ForAction(action) ?? []).Select(item => new ProjectAssetRow(item, action,
            _projectDirectory, item.Issue == warning ? null : DescribeIssue(item.Issue), UpdateSelection))];
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        ProjectAssetAction action = Action;
        bool confirming = DeleteConfirmation.Value != null;
        bool canEdit = !IsBusy.Value && !confirming;
        CanEdit.Value = canEdit;
        int selectableCount = Rows.Value.Count(row => row.CanSelect);
        int selectedCount = Rows.Value.Count(row => row.IsSelected);
        CanSelect.Value = canEdit && selectableCount > 0;
        AllSelected.Value = selectedCount == 0 ? false : selectedCount == selectableCount ? true : null;
        CanApply.Value = !IsBusy.Value && Rows.Value.Any(row => row.IsSelected && row.Item.CanApply(confirming ? ProjectAssetAction.Delete : action));
        CanUseSecondary.Value = !IsBusy.Value && action == ProjectAssetAction.Restore
            && (confirming || Rows.Value.Any(row => row.IsSelected && row.Item.CanApply(ProjectAssetAction.Delete)));
        ApplyLabel.Value = confirming ? ProjectAssetsStrings.DeletePermanently : action switch
        {
            ProjectAssetAction.Collect => ProjectAssetsStrings.Copy,
            ProjectAssetAction.Quarantine => ProjectAssetsStrings.MoveAside,
            _ => ProjectAssetsStrings.Restore,
        };
        SecondaryLabel.Value = action == ProjectAssetAction.Restore
            ? confirming ? Strings.Cancel : ProjectAssetsStrings.DeletePermanently : null;
        IsEmpty.Value = !IsBusy.Value && Error.Value == null && Rows.Value.Length == 0;
        SelectionText.Value = string.Format(ProjectAssetsStrings.Selection, selectedCount,
            selectableCount, MediaFileInfo.FormatFileSize(Rows.Value.Where(row => row.IsSelected).Sum(row => row.Item.Size)));
    }

    internal static string? DescribeIssue(ProjectAssetIssue? issue) => issue switch
    {
        ProjectAssetIssue.Missing => ProjectAssetsStrings.Missing,
        ProjectAssetIssue.UnsafePath => ProjectAssetsStrings.UnsafePath,
        ProjectAssetIssue.SharedReference => ProjectAssetsStrings.SharedReference,
        ProjectAssetIssue.IncompleteReferences => ProjectAssetsStrings.IncompleteReferences,
        ProjectAssetIssue.EditHistory => ProjectAssetsStrings.EditHistory,
        ProjectAssetIssue.InUse => ProjectAssetsStrings.InUse,
        ProjectAssetIssue.DestinationExists => ProjectAssetsStrings.DestinationExists,
        ProjectAssetIssue.Unavailable => ProjectAssetsStrings.Unavailable,
        _ => null,
    };

    public void Dispose()
    {
        _modeSubscription.Dispose();
        Cancel();
    }
}

internal sealed class ProjectAssetRow(ProjectAssetItem item, ProjectAssetAction action, string projectDirectory,
    string? issue, Action onSelected) : INotifyPropertyChanged
{
    private bool _selected = action == ProjectAssetAction.Collect && item.CanApply(action);

    public ProjectAssetItem Item => item;
    public string Name => Path.GetFileName(item.Path);
    public string Source => action == ProjectAssetAction.Restore ? ProjectAssetsStrings.QuarantineFolder : DisplayPath(item.Path);
    public string Destination => action == ProjectAssetAction.Quarantine ? ProjectAssetsStrings.QuarantineFolder : DisplayPath(item.Destination);
    public string SourcePaths => string.Join(Environment.NewLine, item.Files.Select(file => file.Path));
    public string DestinationPaths => string.Join(Environment.NewLine, item.Files.Select(file => file.Destination));
    public string? Issue => issue;
    public string Size => item.Files.All(file => file.Stamp == null) ? "" : item.Files.Length > 1
        ? string.Format(ProjectAssetsStrings.BundleSize, item.Files.Length, MediaFileInfo.FormatFileSize(item.Size))
        : MediaFileInfo.FormatFileSize(item.Size);
    public bool CanSelect => item.CanApply(action) || action == ProjectAssetAction.Restore && item.CanApply(ProjectAssetAction.Delete);
    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (SetSelected(value)) onSelected();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private string DisplayPath(string path) => ProjectAssetPaths.IsBelow(projectDirectory, path)
        ? Path.GetRelativePath(projectDirectory, path) : path;

    internal bool SetSelected(bool value)
    {
        if (!CanSelect || _selected == value) return false;
        _selected = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        return true;
    }
}
