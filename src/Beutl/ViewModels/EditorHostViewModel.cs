using System.Collections.Specialized;
using Avalonia.Threading;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public class EditorHostViewModel : IDisposable, IAsyncDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<EditorHostViewModel>();
    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly object _lifetimeGate = new();
    private readonly TaskCompletionSource _asyncDisposalCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private Project? _subscribedProject;
    private TaskCompletionSource? _operationsDrained;
    private int _ownedOperations;
    private int _asyncDisposalStarted;
    private bool _disposeRequested;

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
        return RunOwnedOperationAsync(async () =>
        {
            Project? project = _projectService.CurrentProject.Value;
            CoreObject? selectedObject = _editorService.SelectedTabItem.Value?.Context.Value?.Object;
            if (project is not null)
            {
                closeContext.RegisterCompletion(projectClosed =>
                    RestoreAfterAbortedCloseAsync(project, selectedObject, projectClosed));
            }

            await DispatchProjectChangeAsync(null, project);
        });
    }

    private Task OnProjectOpenedAsync(Project project)
    {
        return RunOwnedOperationAsync(() => DispatchProjectChangeAsync(project, null));
    }

    private async Task DispatchProjectChangeAsync(Project? @new, Project? old)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await OnProjectChangedAsync(@new, old);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(() => OnProjectChangedAsync(@new, old));
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

        return RunOwnedOperationAsync(() => DispatchProjectRestoreAsync(project, selectedObject));
    }

    private async Task DispatchProjectRestoreAsync(Project project, CoreObject? selectedObject)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await RestoreProjectAsync(project, selectedObject);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(() => RestoreProjectAsync(project, selectedObject));
        }
    }

    private async Task RestoreProjectAsync(Project project, CoreObject? selectedObject)
    {
        if (!ReferenceEquals(_projectService.CurrentProject.Value, project))
        {
            return;
        }

        await OnProjectChangedAsync(project, null);
        if (selectedObject is ProjectItem selectedItem && project.Items.Contains(selectedItem))
        {
            _editorService.ActivateTabItem(selectedItem);
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
            throw;
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
            throw;
        }
    }

    private void Project_Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = RunOwnedOperationAsync(() => HandleProjectItemsChangedAsync(e));
    }

    public void Dispose()
    {
        _ = ObserveDisposalAsync(StartDisposal());
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(StartDisposal());
    }

    private Task StartDisposal()
    {
        Task operationsDrained = BeginDispose();
        if (Interlocked.CompareExchange(ref _asyncDisposalStarted, 1, 0) == 0)
        {
            _ = CompleteDisposalAsync(operationsDrained);
        }

        return _asyncDisposalCompletion.Task;
    }

    private static async Task ObserveDisposalAsync(Task disposal)
    {
        try
        {
            await disposal;
        }
        catch
        {
            // CompleteDisposalAsync already logged the failure. This observes it for sync Dispose.
        }
    }

    private async Task CompleteDisposalAsync(Task operationsDrained)
    {
        try
        {
            await operationsDrained;
            await DispatchProjectChangeAsync(null, _projectService.CurrentProject.Value);
            _asyncDisposalCompletion.TrySetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose the editor host.");
            _asyncDisposalCompletion.TrySetException(ex);
        }
    }

    private Task RunOwnedOperationAsync(Func<Task> operation)
    {
        lock (_lifetimeGate)
        {
            if (_disposeRequested)
            {
                return Task.CompletedTask;
            }

            _ownedOperations++;
        }

        return CompleteOwnedOperationAsync(operation);
    }

    private async Task CompleteOwnedOperationAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        finally
        {
            TaskCompletionSource? operationsDrained = null;
            lock (_lifetimeGate)
            {
                _ownedOperations--;
                if (_disposeRequested && _ownedOperations == 0)
                {
                    operationsDrained = _operationsDrained;
                }
            }

            operationsDrained?.TrySetResult();
        }
    }

    private Task BeginDispose()
    {
        lock (_lifetimeGate)
        {
            if (!_disposeRequested)
            {
                _disposeRequested = true;
                _projectService.Closing -= OnProjectClosingAsync;
                _projectService.Opened -= OnProjectOpenedAsync;
                if (_subscribedProject is { } project)
                {
                    project.Items.CollectionChanged -= Project_Items_CollectionChanged;
                    _subscribedProject = null;
                }
            }

            if (_ownedOperations == 0)
            {
                return Task.CompletedTask;
            }

            return (_operationsDrained ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }

    private void SubscribeToProject(Project project)
    {
        lock (_lifetimeGate)
        {
            if (_disposeRequested || ReferenceEquals(_subscribedProject, project))
            {
                return;
            }

            if (_subscribedProject is { } previous)
            {
                previous.Items.CollectionChanged -= Project_Items_CollectionChanged;
            }

            project.Items.CollectionChanged += Project_Items_CollectionChanged;
            _subscribedProject = project;
        }
    }

    private void UnsubscribeFromProject(Project project)
    {
        lock (_lifetimeGate)
        {
            if (ReferenceEquals(_subscribedProject, project))
            {
                project.Items.CollectionChanged -= Project_Items_CollectionChanged;
                _subscribedProject = null;
            }
        }
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

    // Uri.LocalPath throws InvalidOperationException for relative URIs; protect log
    // formatting inside catch blocks from masking the original exception.
    private static string? SafeLocalPath(Uri? uri)
    {
        if (uri is null)
            return null;
        return uri.IsAbsoluteUri ? uri.LocalPath : uri.OriginalString;
    }
}
