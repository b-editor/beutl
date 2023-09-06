using System.Diagnostics.CodeAnalysis;

using Beutl.Configuration;
using Beutl.Services;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    [MemberNotNull(nameof(CloseFile), nameof(CloseProject), nameof(Save), nameof(SaveAll))]
    private void InitializeFilesCommands()
    {
        CloseFile = new ReactiveCommand(EditorService.Current.SelectedTabItem.Select(i => i != null))
            .WithSubscribe(OnCloseFile);

        CloseProject = new ReactiveCommand(IsProjectOpened)
            .WithSubscribe(ProjectService.Current.CloseProject);

        Save = new AsyncReactiveCommand(IsProjectOpened)
            .WithSubscribe(OnSave);

        SaveAll = new AsyncReactiveCommand(IsProjectOpened)
            .WithSubscribe(OnSaveAll);

        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        viewConfig.RecentFiles.ForEachItem(
            item => RecentFileItems.Insert(0, item),
            item => RecentFileItems.Remove(item),
            RecentFileItems.Clear);

        viewConfig.RecentProjects.ForEachItem(
            item => RecentProjectItems.Insert(0, item),
            item => RecentProjectItems.Remove(item),
            RecentProjectItems.Clear);

        OpenRecentFile.Subscribe(file => EditorService.Current.ActivateTabItem(file, TabOpenMode.YourSelf));

        OpenRecentProject.Subscribe(file =>
        {
            if (!File.Exists(file))
            {
                NotificationService.ShowInformation("", Message.FileDoesNotExist);
            }
            else if (ProjectService.Current.OpenProject(file) == null)
            {
                NotificationService.ShowInformation("", Message.CouldNotOpenProject);
            }
        });
    }

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
    public ReactiveCommand CreateNewProject { get; } = new();

    public ReactiveCommand CreateNew { get; } = new();

    public ReactiveCommand OpenProject { get; } = new();

    public ReactiveCommand OpenFile { get; } = new();

    public ReactiveCommand CloseFile { get; private set; }

    public ReactiveCommand CloseProject { get; private set; }

    public AsyncReactiveCommand Save { get; private set; }

    public AsyncReactiveCommand SaveAll { get; private set; }

    public ReactiveCommand<string> OpenRecentFile { get; } = new();

    public ReactiveCommand<string> OpenRecentProject { get; } = new();

    public CoreList<string> RecentFileItems { get; } = new();

    public CoreList<string> RecentProjectItems { get; } = new();

    public ReactiveCommand Exit { get; } = new();

    private async Task OnSaveAll()
    {
        Project? project = ProjectService.Current.CurrentProject.Value;
        int itemsCount = 0;

        try
        {
            project?.Save(project.FileName);
            itemsCount++;

            foreach (EditorTabItem? item in EditorService.Current.TabItems)
            {
                if (item.Commands.Value != null)
                {
                    if (await item.Commands.Value.OnSave())
                    {
                        itemsCount++;
                    }
                    else
                    {
                        Type type = item.Extension.Value.GetType();
                        _logger.Error("{Extension} failed to save file", type.FullName ?? type.Name);
                        NotificationService.ShowError(Message.Unable_to_save_file, item.FileName.Value);
                    }
                }
            }

            NotificationService.ShowSuccess(string.Empty, string.Format(Message.ItemsSaved, itemsCount.ToString()));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save files");
            NotificationService.ShowError(string.Empty, Message.OperationCouldNotBeExecuted);
        }
    }

    private async Task OnSave()
    {
        EditorTabItem? item = EditorService.Current.SelectedTabItem.Value;
        if (item != null)
        {
            try
            {
                bool result = await (item.Commands.Value == null ? ValueTask.FromResult(false) : item.Commands.Value.OnSave());

                if (result)
                {
                    NotificationService.ShowSuccess(string.Empty, string.Format(Message.ItemSaved, item.FileName));
                }
                else
                {
                    Type type = item.Extension.Value.GetType();
                    _logger.Error("{Extension} failed to save file", type.FullName ?? type.Name);
                    NotificationService.ShowInformation(string.Empty, Message.OperationCouldNotBeExecuted);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save file");
                NotificationService.ShowError(string.Empty, Message.OperationCouldNotBeExecuted);
            }
        }
    }

    private static void OnCloseFile()
    {
        EditorTabItem? tabItem = EditorService.Current.SelectedTabItem.Value;
        if (tabItem != null)
        {
            EditorService.Current.CloseTabItem(
                tabItem.FilePath.Value,
                tabItem.TabOpenMode);
        }
    }
}
