using System.Diagnostics.CodeAnalysis;

using Beutl.Configuration;
using Beutl.Services;

using OpenTelemetry.Trace;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    [MemberNotNull(nameof(CloseFile), nameof(CloseProject), nameof(Save), nameof(SaveAll))]
    private void InitializeFilesCommands()
    {
        CloseFile = new ReactiveCommandSlim(EditorService.Current.SelectedTabItem.Select(i => i != null))
            .WithSubscribe(OnCloseFile);

        CloseProject = new ReactiveCommandSlim(IsProjectOpened)
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
    public ReactiveCommandSlim CreateNewProject { get; } = new();

    public ReactiveCommandSlim CreateNew { get; } = new();

    public ReactiveCommandSlim OpenProject { get; } = new();

    public ReactiveCommandSlim OpenFile { get; } = new();

    public ReactiveCommandSlim CloseFile { get; private set; }

    public ReactiveCommandSlim CloseProject { get; private set; }

    public AsyncReactiveCommand Save { get; private set; }

    public AsyncReactiveCommand SaveAll { get; private set; }

    public ReactiveCommandSlim<string> OpenRecentFile { get; } = new();

    public ReactiveCommandSlim<string> OpenRecentProject { get; } = new();

    public CoreList<string> RecentFileItems { get; } = [];

    public CoreList<string> RecentProjectItems { get; } = [];

    public ReactiveCommandSlim Exit { get; } = new();

    private async Task OnSaveAll()
    {
        using Activity? activity = Telemetry.StartActivity("SaveAll");
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
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.RecordException(ex);
            _logger.Error(ex, "Failed to save files");
            NotificationService.ShowError(string.Empty, Message.OperationCouldNotBeExecuted);
        }
        finally
        {
            activity?.SetTag("itemsCount", itemsCount);
        }
    }

    private async Task OnSave()
    {
        using Activity? activity = Telemetry.StartActivity("Save");
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
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.RecordException(ex);
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
