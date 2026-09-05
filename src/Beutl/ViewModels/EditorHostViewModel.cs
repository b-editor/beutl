using System.Collections.Specialized;
using Avalonia.Threading;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public class EditorHostViewModel : IAsyncDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<EditorHostViewModel>();
    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly ProjectService.ProjectChangeRegistration _projectChangeRegistration;
    private readonly object _projectChangeGate = new();
    private Task _projectChangeTask = Task.CompletedTask;
    private Task? _disposeTask;
    private object? _activeProjectItems;
    private int _disposed;

    public EditorHostViewModel(ProjectService projectService, EditorService editorService)
    {
        _projectService = projectService;
        _editorService = editorService;
        _projectChangeRegistration = _projectService.RegisterProjectChangeHandler(
            new ProjectChangeHandler(this));
    }

    internal Task WaitForPendingProjectChangesAsync()
    {
        lock (_projectChangeGate)
        {
            return _projectChangeTask;
        }
    }

    private Task QueueProjectChange(Project? @new, Project? old)
    {
        Task previous;
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_projectChangeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            previous = _projectChangeTask;
            _projectChangeTask = completion.Task;
        }
        _ = AwaitPreviousAndApplyAsync(previous, @new, old, completion);
        return completion.Task;
    }

    private async Task AwaitPreviousAndApplyAsync(
        Task previous,
        Project? @new,
        Project? old,
        TaskCompletionSource completion)
    {
        try
        {
            await previous;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A previous project change failed before the next change could run.");
        }

        try
        {
            await RunOnUiThreadAsync(() => OnProjectChangedAsync(@new, old));
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    public IReactiveProperty<EditorTabItem?> SelectedTabItem => _editorService.SelectedTabItem;

    private async Task OnProjectChangedAsync(Project? @new, Project? old)
    {
        _activeProjectItems = @new?.Items;
        if (old is not null)
        {
            old.Items.CollectionChanged -= Project_Items_CollectionChanged;
        }

        if (@new is not null)
        {
            @new.Items.CollectionChanged += Project_Items_CollectionChanged;
        }

        try
        {
            CoreObject[] items = @new?.Items.Cast<CoreObject>().ToArray() ?? [];
            await _editorService.ReconcileTabItemsAsync(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to reconcile editor tabs while changing projects. OldProject={OldProject} NewProject={NewProject}",
                SafeLocalPath(old?.Uri),
                SafeLocalPath(@new?.Uri));
            NotificationService.ShowError(Strings.Project, MessageStrings.OperationFailed);
        }
    }

    private void Project_Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ProjectItemsChange change = ProjectItemsChange.Capture(sender, e);
        QueueProjectItemChange(sender, change);
    }

    private void QueueProjectItemChange(object? sender, ProjectItemsChange change)
    {
        Task previous;
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_projectChangeGate)
        {
            if (_disposed != 0)
                return;

            previous = _projectChangeTask;
            _projectChangeTask = completion.Task;
        }
        _ = AwaitPreviousAndHandleItemsAsync(previous, sender, change, completion);
    }

    private async Task AwaitPreviousAndHandleItemsAsync(
        Task previous,
        object? sender,
        ProjectItemsChange change,
        TaskCompletionSource completion)
    {
        try
        {
            await previous;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A previous project change failed before an item change could run.");
        }

        if (!ReferenceEquals(sender, _activeProjectItems))
        {
            completion.TrySetResult();
            return;
        }

        try
        {
            await RunOnUiThreadAsync(() => HandleProjectItemsChangedAsync(change));
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private static async Task RunOnUiThreadAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await action();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(action);
        }
    }

    private async Task HandleProjectItemsChangedAsync(ProjectItemsChange change)
    {
        try
        {
            if (change.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (ProjectItem item in change.NewItems)
                {
                    _editorService.ActivateTabItem(item);
                }
            }
            else if (change.Action == NotifyCollectionChangedAction.Remove)
            {
                await CloseProjectItemsAsync(change.OldItems);
            }
            else if (change.Action == NotifyCollectionChangedAction.Replace)
            {
                await _editorService.ReconcileTabItemsAsync(change.CurrentItems);
            }
            else if (change.Action == NotifyCollectionChangedAction.Reset)
            {
                await _editorService.ReconcileTabItemsAsync(change.CurrentItems);
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
                change.Action);
            NotificationService.ShowError(Strings.Project, MessageStrings.OperationFailed);
        }
    }

    private async Task CloseProjectItemsAsync(IEnumerable<ProjectItem> items)
    {
        foreach (ProjectItem item in items)
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

    public ValueTask DisposeAsync()
    {
        Task task;
        TaskCompletionSource? completion = null;
        lock (_projectChangeGate)
        {
            if (_disposeTask is null)
            {
                completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = completion.Task;
            }

            task = _disposeTask;
        }

        if (completion is not null)
        {
            _ = DisposeCoreAsync(completion);
        }

        return new ValueTask(task);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        Exception? failure = null;
        try
        {
            await _projectChangeRegistration.BeginDisposeAsync();

            Task pending;
            lock (_projectChangeGate)
            {
                _disposed = 1;
                pending = _projectChangeTask;
            }

            await pending;
            await RunOnUiThreadAsync(async () =>
            {
                DetachActiveProjectItems();
                await _editorService.ReconcileTabItemsAsync([]);
            });
        }
        catch (Exception ex)
        {
            failure = ex;
            lock (_projectChangeGate)
            {
                _disposed = 1;
            }

            try
            {
                await RunOnUiThreadAsync(async () =>
                {
                    DetachActiveProjectItems();
                    await _editorService.ReconcileTabItemsAsync([]);
                });
            }
            catch (Exception cleanupEx)
            {
                failure = new AggregateException(ex, cleanupEx);
            }
        }
        finally
        {
            _projectChangeRegistration.CompleteDispose();
            if (failure is null)
                completion.TrySetResult();
            else
                completion.TrySetException(failure);
        }
    }

    private void DetachActiveProjectItems()
    {
        if (_activeProjectItems is INotifyCollectionChanged items)
        {
            items.CollectionChanged -= Project_Items_CollectionChanged;
        }

        _activeProjectItems = null;
    }

    private readonly record struct ProjectItemsChange(
        NotifyCollectionChangedAction Action,
        ProjectItem[] NewItems,
        ProjectItem[] OldItems,
        ProjectItem[] CurrentItems)
    {
        public static ProjectItemsChange Capture(object? sender, NotifyCollectionChangedEventArgs args)
        {
            ProjectItem[] newItems = args.NewItems?.OfType<ProjectItem>().ToArray() ?? [];
            ProjectItem[] oldItems = args.OldItems?.OfType<ProjectItem>().ToArray() ?? [];
            ProjectItem[] currentItems = (args.Action is
                NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Replace)
                && sender is IEnumerable<ProjectItem> items
                    ? items.ToArray()
                    : [];
            return new ProjectItemsChange(args.Action, newItems, oldItems, currentItems);
        }
    }

    private sealed class ProjectChangeHandler(EditorHostViewModel owner) : IProjectChangeHandler
    {
        public Task ApplyProjectChangeAsync(Project? @new, Project? old)
            => owner.QueueProjectChange(@new, old);

        public Task WaitForPendingProjectChangesAsync()
            => owner.WaitForPendingProjectChangesAsync();
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
