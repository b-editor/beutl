using System.Diagnostics.CodeAnalysis;
using Beutl.Configuration;
using Beutl.Editor.VersionControl;
using Beutl.Serialization;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    [MemberNotNull(
        nameof(CloseFile),
        nameof(CloseProject),
        nameof(Save),
        nameof(SaveAll),
        nameof(EnableVersionControl),
        nameof(CommitVersion),
        nameof(ExportProject))]
    private void InitializeFilesCommands()
    {
        CloseFile = new ReactiveCommandSlim(_editorService.SelectedTabItem.Select(i => i != null))
            .WithSubscribe(() => OnCloseFileCore(null));

        CloseFileCore = new ReactiveCommandSlim<EditorTabItem>()
            .WithSubscribe(OnCloseFileCore);

        CloseProject = new AsyncReactiveCommand(IsProjectOpened)
            .WithSubscribe(() => _projectService.CloseProject());

        Save = new AsyncReactiveCommand(IsProjectOpened)
            .WithSubscribe(OnSave);

        SaveAll = new AsyncReactiveCommand(IsProjectOpened)
            .WithSubscribe(OnSaveAll);

        IObservable<bool> canEnableVersionControl = IsProjectOpened.CombineLatest(
            _versionControlSession.IsGitAvailable,
            _versionControlSession.IsTracked,
            static (isOpened, isGitAvailable, isTracked) =>
                isOpened && isGitAvailable && !isTracked);
        EnableVersionControl = new AsyncReactiveCommand(canEnableVersionControl);
        IObservable<bool> canCommitVersion = IsProjectOpened.CombineLatest(
            _versionControlSession.IsGitAvailable,
            _versionControlSession.IsTracked,
            static (isOpened, isGitAvailable, isTracked) =>
                isOpened && isGitAvailable && isTracked);
        CommitVersion = new AsyncReactiveCommand(canCommitVersion);

        ExportProject = new AsyncReactiveCommand(IsProjectOpened);

        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        viewConfig.RecentFiles.ForEachItem(
            item => RecentFileItems.Insert(0, item),
            item => RecentFileItems.Remove(item),
            RecentFileItems.Clear);

        viewConfig.RecentProjects.ForEachItem(
            item => RecentProjectItems.Insert(0, item),
            item => RecentProjectItems.Remove(item),
            RecentProjectItems.Clear);

        OpenRecentFile.Subscribe(OpenFileCore);

        OpenRecentProject.Subscribe(async file =>
        {
            await _projectService.OpenProject(file);
        });
    }

    public ReactiveCommandSlim<EditorTabItem> CloseFileCore { get; set; }

    // File
    //    Create new
    //       Project
    //       File
    //    Open
    //       Project
    //       File
    //    Close
    //    Close project
    //    Save
    //    Save all
    //    Recent files
    //    Recent projects
    //    Exit
    public ReactiveCommandSlim CreateNewProject { get; } = new();

    public ReactiveCommandSlim CreateNew { get; } = new();

    public ReactiveCommandSlim OpenProject { get; } = new();

    public ReactiveCommandSlim OpenFile { get; } = new();

    public ReactiveCommandSlim CloseFile { get; private set; }

    public AsyncReactiveCommand CloseProject { get; private set; }

    public AsyncReactiveCommand Save { get; private set; }

    public AsyncReactiveCommand SaveAll { get; private set; }

    public AsyncReactiveCommand EnableVersionControl { get; private set; }

    public AsyncReactiveCommand CommitVersion { get; private set; }

    public ReactiveCommandSlim<string> OpenRecentFile { get; } = new();

    public AsyncReactiveCommand<string> OpenRecentProject { get; } = new();

    public CoreList<string> RecentFileItems { get; } = [];

    public CoreList<string> RecentProjectItems { get; } = [];

    public ReactiveCommandSlim Exit { get; } = new();

    public AsyncReactiveCommand ExportProject { get; private set; } = null!;

    public AsyncReactiveCommand ImportProject { get; } = new();

    private async Task OnSaveAll()
    {
        using Activity? activity = Telemetry.StartActivity("SaveAll");
        int itemsCount = 0;
        bool allRequestedSavesSucceeded = true;

        try
        {
            using IProjectFileWriteLease fileWrite = await _editorService.BeginProjectFileWriteAsync(
                CancellationToken.None);
            // Waiting for the lease can span a whole version-control transition, which closes the
            // project and reopens a new instance, so nothing may be captured before the wait.
            Project? project = _projectService.CurrentProject.Value;
            if (project != null)
            {
                CoreSerializer.StoreToUri(project, project.Uri!);
            }

            itemsCount++;

            // Each OnSave yields to the dispatcher, which can close a tab, so the live list is
            // snapshotted rather than enumerated across the awaits.
            foreach (EditorTabItem item in _editorService.TabItems.ToArray())
            {
                if (item.Commands.Value is { } commands)
                {
                    if (await commands.OnSave())
                    {
                        itemsCount++;
                    }
                    else
                    {
                        allRequestedSavesSucceeded = false;
                        LogFailedSave(item);
                        NotificationService.ShowError(MessageStrings.UnableToSaveFile, item.FileName.Value);
                    }
                }
            }

            NotificationService.ShowSuccess(string.Empty, string.Format(MessageStrings.ItemsSaved, itemsCount.ToString()));

            if (GlobalConfiguration.Instance.EditorConfig.IsAutoSaveEnabled
                && _editorService.TabItems.All(v => v.Context.Value is ISupportAutoSaveEditorContext))
            {
                NotificationService.ShowInformation(string.Empty, MessageStrings.FilesAutoSaved);
            }

            if (allRequestedSavesSucceeded)
            {
                await _versionControlSession.NotifySavedAsync(fileWrite);
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "Failed to save files");
            NotificationService.ShowError(string.Empty, MessageStrings.OperationFailed);
        }
        finally
        {
            activity?.SetTag("itemsCount", itemsCount);
        }
    }

    private async Task OnSave()
    {
        using Activity? activity = Telemetry.StartActivity("Save");
        if (_editorService.SelectedTabItem.Value == null)
        {
            return;
        }

        EditorTabItem? item = null;
        try
        {
            using IProjectFileWriteLease fileWrite = await _editorService.BeginProjectFileWriteAsync(
                CancellationToken.None);
            // The tab captured before the wait may have been disposed by a version-control
            // transition, so the save targets whichever tab is selected once the lease is held.
            item = _editorService.SelectedTabItem.Value;
            if (item == null)
            {
                return;
            }

            bool result = item.Commands.Value is { } commands && await commands.OnSave();
            if (result)
            {
                NotificationService.ShowSuccess(string.Empty, string.Format(MessageStrings.ItemSaved, item.FileName.Value));

                if (GlobalConfiguration.Instance.EditorConfig.IsAutoSaveEnabled
                    && item.Context.Value is ISupportAutoSaveEditorContext)
                {
                    NotificationService.ShowInformation(string.Empty, MessageStrings.FilesAutoSaved);
                }

                await _versionControlSession.NotifySavedAsync(fileWrite);
            }
            else
            {
                LogFailedSave(item);
                NotificationService.ShowInformation(string.Empty, MessageStrings.OperationFailed);
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "Failed to save file: {FileName}", item?.FileName.Value);
            NotificationService.ShowError(string.Empty, MessageStrings.OperationFailed);
        }
    }

    private void LogFailedSave(EditorTabItem item)
    {
        // Extension is typed non-nullable but is a projection of Context, which tab teardown nulls.
        Type? type = item.Extension.Value?.GetType();
        _logger.LogError(
            "{Extension} failed to save file: {FileName}",
            type?.FullName ?? type?.Name ?? "(unknown)",
            item.FileName.Value);
    }

    internal void OpenFileCore(string file)
    {
        try
        {
            Project? project = _projectService.CurrentProject.Value;

            var uri = UriHelper.CreateFromPath(file);
            ProjectItem? projItem = null;
            if (project != null)
                projItem = project.Items.FirstOrDefault(i => i.Uri == uri);

            projItem ??= CoreSerializer.RestoreFromUri<ProjectItem>(uri);

            if (project != null)
            {
                ProjectPersistence.AddItemAndPersist(project, projItem);
            }

            _editorService.ActivateTabItem(projItem);
        }
        catch (Exception ex)
        {
            _ = ex.Handle();
        }
    }

    private async void OnCloseFileCore(EditorTabItem? item)
    {
        if (IsProjectOpened.Value)
        {
            RemoveFromProject.Execute(item);
        }
        else
        {
            EditorTabItem? tabItem = item ?? _editorService.SelectedTabItem.Value;
            if (tabItem != null)
            {
                await _editorService.CloseTabItem(tabItem);
            }
        }
    }
}
