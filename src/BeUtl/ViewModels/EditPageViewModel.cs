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
    }

    public IReactiveProperty<Project?> Project => _projectService.CurrentProject;

    public IReadOnlyReactiveProperty<bool> IsProjectOpened => _projectService.IsOpened;

    public ICoreList<TabViewModel> TabItems => _editorService.TabItems;

    public IReactiveProperty<TabViewModel?> SelectedTabItem => _editorService.SelectedTabItem;

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
