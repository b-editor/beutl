using System.Reactive.Linq;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;

using BeUtl.Collections;
using BeUtl.Configuration;
using BeUtl.Framework;
using BeUtl.Framework.Services;
using BeUtl.ProjectSystem;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using BeUtl.Framework.Service;
using BeUtl.Pages;

namespace BeUtl.ViewModels;

public sealed class EditPageViewModel
{
    public class TabViewModel : IDisposable
    {
        public TabViewModel(IEditorContext context)
        {
            Context = context;
        }

        public IEditorContext Context { get; }

        public string FilePath => Context.EdittingFile;

        public string FileName => Path.GetFileName(FilePath);

        public EditorExtension Extension => Context.Extension;

        public IKnownEditorCommands? Commands => Context.Commands;

        public ReactivePropertySlim<bool> IsSelected { get; } = new();

        public void Dispose()
        {
        }
    }

    private readonly IProjectService _projectService;

    public EditPageViewModel()
    {
        _projectService = ServiceLocator.Current.GetRequiredService<IProjectService>();
        TabItems = new();
        _projectService.ProjectObservable.Subscribe(item => ProjectChanged(item.New, item.Old));
        Save = new(_projectService.IsOpened);
        SaveAll = new(_projectService.IsOpened);
        Undo = new(_projectService.IsOpened);
        Redo = new(_projectService.IsOpened);
        KnownCommands = SelectedTabItem.Select(i => i?.Commands).ToReadOnlyReactivePropertySlim();

        Save.Subscribe(async () =>
        {
            TabViewModel? item = SelectedTabItem.Value;
            if (item != null)
            {
                INotificationService nservice = ServiceLocator.Current.GetRequiredService<INotificationService>();
                try
                {
                    bool result = await (item.Commands == null ? ValueTask.FromResult(false) : item.Commands.OnSave());

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
                    if (item.Commands != null
                        && await item.Commands.OnSave())
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

            if (KnownCommands.Value != null)
                handled = await KnownCommands.Value.OnUndo();

            if (!handled)
                CommandRecorder.Default.Undo();
        });
        Redo.Subscribe(async () =>
        {
            bool handled = false;

            if (KnownCommands.Value != null)
                handled = await KnownCommands.Value.OnRedo();

            if (!handled)
                CommandRecorder.Default.Redo();
        });
    }

    public IReactiveProperty<Project?> Project => _projectService.CurrentProject;

    public IReadOnlyReactiveProperty<bool> IsProjectOpened => _projectService.IsOpened;

    public CoreList<TabViewModel> TabItems { get; }

    public ReactiveProperty<TabViewModel?> SelectedTabItem { get; } = new();

    public ReadOnlyReactivePropertySlim<IKnownEditorCommands?> KnownCommands { get; }

    public ReactiveCommand Save { get; }

    public ReactiveCommand SaveAll { get; }

    public ReactiveCommand Undo { get; }

    public ReactiveCommand Redo { get; }

    private void ProjectChanged(Project? @new, Project? old)
    {
        // プロジェクトが開いた
        if (@new != null)
        {
            @new.Children.CollectionChanged += Project_Children_CollectionChanged;
            foreach (Scene item in @new.Children)
            {
                SelectOrAddTabItem(item.FileName);
            }
        }

        // プロジェクトが閉じた
        if (old != null)
        {
            old.Children.CollectionChanged -= Project_Children_CollectionChanged;
            foreach (Scene item in old.Children)
            {
                CloseTabItem(item.FileName);
            }
        }
    }

    private void Project_Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add &&
            e.NewItems != null)
        {
            foreach (Scene item in e.NewItems.OfType<Scene>())
            {
                SelectOrAddTabItem(item.FileName);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove &&
                 e.OldItems != null)
        {
            foreach (Scene item in e.OldItems.OfType<Scene>())
            {
                CloseTabItem(item.FileName);
            }
        }
    }

    public bool TryGetTabItem(string? file, [NotNullWhen(true)] out TabViewModel? result)
    {
        result = TabItems.FirstOrDefault(i => i.FilePath == file);

        return result != null;
    }

    public void SelectOrAddTabItem(string? file)
    {
        if (File.Exists(file))
        {
            ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
            viewConfig.UpdateRecentFile(file);

            if (TryGetTabItem(file, out TabViewModel? tabItem))
            {
                tabItem.IsSelected.Value = true;
            }
            else
            {
                EditorExtension? ext = PackageManager.Instance.ExtensionProvider.MatchEditorExtension(file);

                if (ext?.TryCreateContext(file, out IEditorContext? context) == true)
                {
                    TabItems.Add(new TabViewModel(context)
                    {
                        IsSelected =
                        {
                            Value = true
                        }
                    });
                }
            }
        }
    }

    public void CloseTabItem(string? file)
    {
        if (TryGetTabItem(file, out var item))
        {
            TabItems.Remove(item);
        }
    }
}
