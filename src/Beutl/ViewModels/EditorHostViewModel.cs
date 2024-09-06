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

    private void ProjectChanged(Project? @new, Project? old)
    {
        for (int i = _editorService.TabItems.Count - 1; i >= 0; i--)
        {
            var item = _editorService.TabItems[i];
            _editorService.TabItems.RemoveAt(i);
            item.Dispose();
        }

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
    }

    private void Project_Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
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
                _editorService.CloseTabItem(item.FileName);
            }
        }
    }
}
