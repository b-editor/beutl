using System.Collections.Specialized;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public class EditorHostViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<EditorHostViewModel>();
    private readonly ProjectService _projectService = ProjectService.Current;
    private readonly EditorService _editorService = EditorService.Current;

    public EditorHostViewModel()
    {
        _projectService.ProjectObservable
            .ObserveOnUIDispatcher()
            .Subscribe(item => _ = OnProjectChangedAsync(item.New, item.Old));
    }

    public IReactiveProperty<EditorTabItem?> SelectedTabItem => _editorService.SelectedTabItem;

    private async Task OnProjectChangedAsync(Project? @new, Project? old)
    {
        try
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
                    _editorService.ActivateTabItem(item);
                }
            }

            foreach (var item in oldItems)
            {
                try
                {
                    await item.DisposeAsync();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispose editor tab item.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in ProjectChanged.");
            NotificationService.ShowError(string.Empty, MessageStrings.OperationFailed);
        }
    }

    private void Project_Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = HandleProjectItemsChangedAsync(e);
    }

    private async Task HandleProjectItemsChangedAsync(NotifyCollectionChangedEventArgs e)
    {
        try
        {
            if (e.Action == NotifyCollectionChangedAction.Add &&
                e.NewItems != null)
            {
                foreach (ProjectItem item in e.NewItems.OfType<ProjectItem>())
                {
                    _editorService.ActivateTabItem(item);
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove &&
                     e.OldItems != null)
            {
                foreach (ProjectItem item in e.OldItems.OfType<ProjectItem>())
                {
                    try
                    {
                        await _editorService.CloseTabItem(item);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to close tab for removed project item.");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in Project_Items_CollectionChanged.");
            NotificationService.ShowError(string.Empty, MessageStrings.OperationFailed);
        }
    }
}
