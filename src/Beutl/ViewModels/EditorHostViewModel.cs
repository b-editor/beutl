using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public class EditorHostViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<EditorHostViewModel>();
    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly object _operationGate = new();
    private readonly ConditionalWeakTable<Project, ClosedProjectSelection> _closedSelections = new();
    private Task _operationTail = Task.CompletedTask;
    private Project? _subscribedProject;
    private long _subscriptionGeneration;

    public EditorHostViewModel(ProjectService projectService, EditorService editorService)
    {
        _projectService = projectService;
        _editorService = editorService;
        _projectService.Closing += OnProjectClosingAsync;
        _projectService.Opened += OnProjectOpenedAsync;
    }

    private Task OnProjectClosingAsync(
        ProjectService.ProjectCloseContext closeContext,
        CancellationToken _)
    {
        return QueueOperationAsync(async () =>
        {
            Project? project = _projectService.CurrentProject.Value;
            CoreObject? selectedObject = _editorService.SelectedTabItem.Value?.Context.Value?.Object;
            if (project is not null)
            {
                // Failed activation may close the target too. Keep each model's selection
                // separately without retaining projects after a successful switch.
                _closedSelections.Remove(project);
                _closedSelections.Add(project, new ClosedProjectSelection(selectedObject));
                closeContext.RegisterCompletion(projectClosed =>
                    RestoreAfterAbortedCloseAsync(project, selectedObject, projectClosed));
            }

            await DispatchProjectChangeAsync(null, project);
        });
    }

    private Task OnProjectOpenedAsync(Project project)
    {
        return QueueOperationAsync(async () =>
        {
            await DispatchProjectChangeAsync(project, null);
            if (_closedSelections.TryGetValue(project, out ClosedProjectSelection? selection))
            {
                _closedSelections.Remove(project);
                if (selection.SelectedObject is ProjectItem item && project.Items.Contains(item))
                    await DispatchAsync(() => _editorService.ActivateTabItem(item));
            }
        });
    }

    private sealed record ClosedProjectSelection(CoreObject? SelectedObject);

    private async Task DispatchProjectChangeAsync(Project? @new, Project? old)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await OnProjectChangedAsync(@new, old);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
                await OnProjectChangedAsync(@new, old));
        }
    }

    private Task RestoreAfterAbortedCloseAsync(
        Project project,
        CoreObject? selectedObject,
        bool projectClosed)
    {
        if (projectClosed || !ReferenceEquals(_projectService.CurrentProject.Value, project))
        {
            return Task.CompletedTask;
        }

        return QueueOperationAsync(async () =>
        {
            await DispatchProjectChangeAsync(project, null);
            if (selectedObject is ProjectItem selectedItem && project.Items.Contains(selectedItem))
            {
                await DispatchAsync(() => _editorService.ActivateTabItem(selectedItem));
            }
        });
    }

    private static async Task DispatchAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(action);
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
                _editorService.SelectedTabItem.Value = null;
                _editorService.TabItems.Clear();

                if (old != null)
                {
                    UnsubscribeFromProject(old);
                }

                if (@new != null)
                {
                    SubscribeToProject(@new);
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
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to dispose editor tab item. FilePath={FilePath}", filePath);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception in {Method}. OldProject={OldProject} NewProject={NewProject}",
                nameof(OnProjectChangedAsync),
                SafeLocalPath(old?.Uri),
                SafeLocalPath(@new?.Uri));
            NotificationService.ShowError(Strings.Project, MessageStrings.OperationFailed);
        }
    }

    private void Project_Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        long generation;
        lock (_operationGate)
        {
            generation = _subscriptionGeneration;
        }

        _ = QueueOperationAsync(() => HandleProjectItemsChangedAsync(sender, e, generation));
    }

    private async Task HandleProjectItemsChangedAsync(
        object? sender,
        NotifyCollectionChangedEventArgs e,
        long generation)
    {
        lock (_operationGate)
        {
            if (generation != _subscriptionGeneration
                || _subscribedProject is null
                || !ReferenceEquals(sender, _subscribedProject.Items))
            {
                return;
            }
        }

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
                        _logger.LogError(
                            ex,
                            "Failed to close tab for removed project item. FilePath={FilePath}",
                            SafeLocalPath(item.Uri));
                    }
                }
            }
            else
            {
                _logger.LogWarning(
                    "Unhandled project items collection change. Action={Action} NewCount={NewCount} OldCount={OldCount}",
                    e.Action,
                    e.NewItems?.Count,
                    e.OldItems?.Count);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception in {Method}. Action={Action}",
                nameof(HandleProjectItemsChangedAsync),
                e.Action);
            NotificationService.ShowError(Strings.Project, MessageStrings.OperationFailed);
        }
    }

    private Task QueueOperationAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Task previous;
        TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_operationGate)
        {
            previous = _operationTail;
            _operationTail = completion.Task;
        }

        _ = CompleteOperationAsync(previous, operation, completion);
        return completion.Task;
    }

    private async Task CompleteOperationAsync(
        Task previous,
        Func<Task> operation,
        TaskCompletionSource completion)
    {
        try
        {
            try
            {
                await previous;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "A previous editor-host operation failed before the next operation ran.");
            }

            await operation();
            completion.TrySetResult();
        }
        catch (OperationCanceledException ex)
        {
            completion.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private void SubscribeToProject(Project project)
    {
        lock (_operationGate)
        {
            if (ReferenceEquals(_subscribedProject, project))
            {
                return;
            }

            if (_subscribedProject is { } previous)
            {
                previous.Items.CollectionChanged -= Project_Items_CollectionChanged;
            }

            project.Items.CollectionChanged += Project_Items_CollectionChanged;
            _subscribedProject = project;
            _subscriptionGeneration++;
        }
    }

    private void UnsubscribeFromProject(Project project)
    {
        lock (_operationGate)
        {
            if (ReferenceEquals(_subscribedProject, project))
            {
                project.Items.CollectionChanged -= Project_Items_CollectionChanged;
                _subscribedProject = null;
                _subscriptionGeneration++;
            }
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
