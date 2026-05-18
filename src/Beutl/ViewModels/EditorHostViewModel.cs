using System.Collections.Specialized;
using Avalonia.Threading;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public class EditorHostViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<EditorHostViewModel>();
    private readonly ProjectService _projectService = ProjectService.Current;
    private readonly EditorService _editorService = EditorService.Current;

    public EditorHostViewModel()
    {
        _projectService.ProjectObservable.Subscribe(item =>
            DispatchProjectChange(item.New, item.Old)
        );
    }

    private void DispatchProjectChange(Project? @new, Project? old)
    {
        // Run inline when already on the UI thread so callers awaiting OpenProject
        // (e.g. RestoreLastProjectTask) still see tabs populated on return. Post only
        // when the notification comes from a background thread.
        if (Dispatcher.UIThread.CheckAccess())
        {
            _ = OnProjectChangedAsync(@new, old);
        }
        else
        {
            Dispatcher.UIThread.Post(() => _ = OnProjectChangedAsync(@new, old));
        }
    }

    public IReactiveProperty<EditorTabItem?> SelectedTabItem => _editorService.SelectedTabItem;

    private async Task OnProjectChangedAsync(Project? @new, Project? old)
    {
        var oldItems = _editorService.TabItems.ToArray();
        try
        {
            try
            {
                _editorService.TabItems.Clear();

                if (old != null)
                {
                    old.Items.CollectionChanged -= Project_Items_CollectionChanged;
                }

                if (@new != null)
                {
                    @new.Items.CollectionChanged += Project_Items_CollectionChanged;
                    foreach (ProjectItem item in @new.Items)
                    {
                        _editorService.ActivateTabItem(item);
                    }
                }
            }
            finally
            {
                foreach (var item in oldItems)
                {
                    // Capture FilePath before DisposeAsync nulls out the underlying context.
                    var filePath = item.FilePath.Value;
                    try
                    {
                        await item.DisposeAsync();
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to dispose editor tab item. FilePath={FilePath}",
                            filePath
                        );
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception in {Method}. OldProject={OldProject} NewProject={NewProject}",
                nameof(OnProjectChangedAsync),
                SafeLocalPath(old?.Uri),
                SafeLocalPath(@new?.Uri)
            );
            NotificationService.ShowError(Strings.Project, MessageStrings.OperationFailed);
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
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (ProjectItem item in e.NewItems.OfType<ProjectItem>())
                {
                    _editorService.ActivateTabItem(item);
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (ProjectItem item in e.OldItems.OfType<ProjectItem>())
                {
                    try
                    {
                        await _editorService.CloseTabItem(item);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to close tab for removed project item. FilePath={FilePath}",
                            SafeLocalPath(item.Uri)
                        );
                    }
                }
            }
            else
            {
                _logger.LogWarning(
                    "Unhandled project items collection change. Action={Action} NewCount={NewCount} OldCount={OldCount}",
                    e.Action,
                    e.NewItems?.Count,
                    e.OldItems?.Count
                );
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception in {Method}. Action={Action}",
                nameof(HandleProjectItemsChangedAsync),
                e.Action
            );
            NotificationService.ShowError(Strings.Project, MessageStrings.OperationFailed);
        }
    }

    // Uri.LocalPath throws InvalidOperationException for relative URIs; protect log
    // formatting inside catch blocks from masking the original exception.
    private static string? SafeLocalPath(Uri? uri)
    {
        if (uri is null)
            return null;
        return uri.IsAbsoluteUri ? uri.LocalPath : uri.OriginalString;
    }
}
