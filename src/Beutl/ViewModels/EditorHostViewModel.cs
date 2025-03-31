using System.Collections.Specialized;
using Beutl.Services;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public class EditorHostViewModel
{
    private readonly ProjectService _projectService = ProjectService.Current;
    private readonly EditorService _editorService = EditorService.Current;

    public EditorHostViewModel()
    {
        _projectService.ProjectObservable.Subscribe(item => ProjectChanged(item.New, item.Old));
    }

    public IReactiveProperty<EditorTabItem?> SelectedTabItem => _editorService.SelectedTabItem;

    private async void ProjectChanged(Project? @new, Project? old)
    {
        var oldItems = _editorService.TabItems.ToArray();
        _editorService.TabItems.Clear();

        // プロジェクトが閉じた
        if (old != null)
        {
            old.Items.CollectionChanged -= Project_Items_CollectionChanged;
        }

        // プロジェクトが開いた
        if (@new != null)
        {
            @new.Items.CollectionChanged += Project_Items_CollectionChanged;
            foreach (ProjectItem item in @new.Items)
            {
                _editorService.ActivateTabItem(item.FileName);
            }
        }

        foreach (var item in oldItems)
        {
            await item.DisposeAsync();
        }
    }

    private async void Project_Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add &&
            e.NewItems != null)
        {
            foreach (ProjectItem item in e.NewItems.OfType<ProjectItem>())
            {
                _editorService.ActivateTabItem(item.FileName);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove &&
                 e.OldItems != null)
        {
            foreach (ProjectItem item in e.OldItems.OfType<ProjectItem>())
            {
                await _editorService.CloseTabItem(item.FileName);
            }
        }
    }
}
