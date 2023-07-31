using System.Collections.Specialized;

using Beutl.Framework;
using Beutl.Framework.Services;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using TabViewModel = Beutl.Services.EditorTabItem;

namespace Beutl.ViewModels;

public sealed class EditPageViewModel : IPageContext
{
    private readonly IProjectService _projectService;
    private readonly EditorService _editorService;

    public EditPageViewModel()
    {
        _projectService = ServiceLocator.Current.GetRequiredService<IProjectService>();
        _editorService = ServiceLocator.Current.GetRequiredService<EditorService>();
        _projectService.ProjectObservable.Subscribe(item => ProjectChanged(item.New, item.Old));
    }

    public IReadOnlyReactiveProperty<Project?> Project => _projectService.CurrentProject;

    public IReadOnlyReactiveProperty<bool> IsProjectOpened => _projectService.IsOpened;

    public ICoreList<TabViewModel> TabItems => _editorService.TabItems;

    public IReactiveProperty<TabViewModel?> SelectedTabItem => _editorService.SelectedTabItem;

    public PageExtension Extension => EditPageExtension.Instance;

    public string Header => Strings.Edit;

    private void ProjectChanged(Project? @new, Project? old)
    {
        // プロジェクトが開いた
        if (@new != null)
        {
            @new.Items.CollectionChanged += Project_Items_CollectionChanged;
            foreach (ProjectItem item in @new.Items)
            {
                SelectOrAddTabItem(item.FileName, TabOpenMode.FromProject);
            }
        }

        // プロジェクトが閉じた
        if (old != null)
        {
            old.Items.CollectionChanged -= Project_Items_CollectionChanged;
            foreach (ProjectItem item in old.Items)
            {
                CloseTabProjectItem(item);
            }
        }
    }

    private void Project_Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add &&
            e.NewItems != null)
        {
            foreach (ProjectItem item in e.NewItems.OfType<ProjectItem>())
            {
                SelectOrAddTabItem(item.FileName, TabOpenMode.FromProject);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove &&
                 e.OldItems != null)
        {
            foreach (ProjectItem item in e.OldItems.OfType<ProjectItem>())
            {
                CloseTabProjectItem(item);
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

    private void CloseTabProjectItem(ProjectItem item)
    {
        if (_editorService.TryGetTabItem(item.FileName, out var tab))
        {
            switch (tab.TabOpenMode)
            {
                case TabOpenMode.FromProject:
                    _editorService.TabItems.Remove(tab);
                    tab.Dispose();
                    break;
                case TabOpenMode.YourSelf:
                    _projectService.Application.Items.Add(item);
                    //((IModifiableHierarchical)item).SetParent(null);
                    //((IModifiableHierarchical)item).SetParent(_projectService.Application);
                    break;
            }
        }
    }

    public void Dispose() => throw new NotImplementedException();
}
