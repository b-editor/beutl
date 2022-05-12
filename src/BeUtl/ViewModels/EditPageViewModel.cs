using System.Collections.Specialized;
using System.Reactive.Linq;

using BeUtl.Collections;
using BeUtl.Framework;
using BeUtl.Framework.Service;
using BeUtl.Framework.Services;
using BeUtl.ProjectSystem;
using BeUtl.Services;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using TabViewModel = BeUtl.Services.EditorTabItem;

namespace BeUtl.ViewModels;

public sealed class EditPageViewModel
{
    private readonly IProjectService _projectService;
    private readonly EditorService _editorService;

    public EditPageViewModel()
    {
        _projectService = ServiceLocator.Current.GetRequiredService<IProjectService>();
        _editorService = ServiceLocator.Current.GetRequiredService<EditorService>();
        _projectService.ProjectObservable.Subscribe(item => ProjectChanged(item.New, item.Old));
        Save = new(_projectService.IsOpened);
        SaveAll = new(_projectService.IsOpened);
        Undo = new(_projectService.IsOpened);
        Redo = new(_projectService.IsOpened);

        Save.Subscribe(async () =>
        {
            TabViewModel? item = SelectedTabItem.Value;
            if (item != null)
            {
                INotificationService nservice = ServiceLocator.Current.GetRequiredService<INotificationService>();
                try
                {
                    bool result = await (item.Commands.Value == null ? ValueTask.FromResult(false) : item.Commands.Value.OnSave());

                    if (result)
                    {
                        string message = new ResourceReference<string>("S.Message.ItemSaved").FindOrDefault("{0}");
                        nservice.Show(new Notification(
                            string.Empty,
                            string.Format(message, item.FileName),
                            NotificationType.Success));
                    }
                    else
                    {
                        string message = new ResourceReference<string>("S.Message.OperationCouldNotBeExecuted").FindOrDefault(string.Empty);
                        nservice.Show(new Notification(
                            string.Empty,
                            message,
                            NotificationType.Information));
                    }
                }
                catch
                {
                    string message = new ResourceReference<string>("S.Message.OperationCouldNotBeExecuted").FindOrDefault(string.Empty);
                    nservice.Show(new Notification(
                        string.Empty,
                        message,
                        NotificationType.Error));
                }
            }
        });

        SaveAll.Subscribe(async () =>
        {
            IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();

            Project? project = service.CurrentProject.Value;
            INotificationService nservice = ServiceLocator.Current.GetRequiredService<INotificationService>();
            int itemsCount = 0;

            try
            {
                project?.Save(project.FileName);
                itemsCount++;

                foreach (TabViewModel? item in TabItems)
                {
                    if (item.Commands.Value != null
                        && await item.Commands.Value.OnSave())
                    {
                        itemsCount++;
                    }
                }

                string message = new ResourceReference<string>("S.Message.ItemsSaved").FindOrDefault(string.Empty);
                nservice.Show(new Notification(
                    string.Empty,
                    string.Format(message, itemsCount.ToString()),
                    NotificationType.Success));
            }
            catch
            {
                string message = new ResourceReference<string>("S.Message.OperationCouldNotBeExecuted").FindOrDefault(string.Empty);
                nservice.Show(new Notification(
                    string.Empty,
                    message,
                    NotificationType.Error));
            }
        });

        Undo.Subscribe(async () =>
        {
            bool handled = false;

            IKnownEditorCommands? commands = SelectedTabItem.Value?.Commands.Value;
            if (commands != null)
                handled = await commands.OnUndo();

            // Todo: EditViewModelにこの処理を移動する
            if (!handled)
                CommandRecorder.Default.Undo();
        });
        Redo.Subscribe(async () =>
        {
            bool handled = false;

            IKnownEditorCommands? commands = SelectedTabItem.Value?.Commands.Value;
            if (commands != null)
                handled = await commands.OnRedo();

            // Todo: EditViewModelにこの処理を移動する
            if (!handled)
                CommandRecorder.Default.Redo();
        });
    }

    public IReactiveProperty<Project?> Project => _projectService.CurrentProject;

    public IReadOnlyReactiveProperty<bool> IsProjectOpened => _projectService.IsOpened;

    public ICoreList<TabViewModel> TabItems => _editorService.TabItems;

    public IReactiveProperty<TabViewModel?> SelectedTabItem => _editorService.SelectedTabItem;

    public ReactiveCommand Save { get; }

    public ReactiveCommand SaveAll { get; }

    public ReactiveCommand Undo { get; }

    public ReactiveCommand Redo { get; }

    private void ProjectChanged(Project? @new, Project? old)
    {
        // プロジェクトが開いた
        if (@new != null)
        {
            @new.Items.CollectionChanged += Project_Items_CollectionChanged;
            foreach (IWorkspaceItem item in @new.Items)
            {
                SelectOrAddTabItem(item.FileName, TabOpenMode.FromProject);
            }
        }

        // プロジェクトが閉じた
        if (old != null)
        {
            old.Items.CollectionChanged -= Project_Items_CollectionChanged;
            foreach (IWorkspaceItem item in old.Items)
            {
                CloseTabItem(item.FileName, TabOpenMode.FromProject);
            }
        }
    }

    private void Project_Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add &&
            e.NewItems != null)
        {
            foreach (IWorkspaceItem item in e.NewItems.OfType<IWorkspaceItem>())
            {
                SelectOrAddTabItem(item.FileName, TabOpenMode.FromProject);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove &&
                 e.OldItems != null)
        {
            foreach (IWorkspaceItem item in e.OldItems.OfType<IWorkspaceItem>())
            {
                CloseTabItem(item.FileName, TabOpenMode.FromProject);
            }
        }
    }

    public void SelectOrAddTabItem(string? file, TabOpenMode tabOpenMode)
    {
        _editorService.ActivateTabItem(file, tabOpenMode);
    }

    public void CloseTabItem(string? file, TabOpenMode tabOpenMode)
    {
        _editorService.CloseTabItem(file, tabOpenMode);
    }
}
