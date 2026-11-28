using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
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
        LifecycleProgress = _editorService.LifecycleActivity
            .Select(CreateLifecycleProgress)
            .ToReadOnlyReactivePropertySlim();
    }

    /// <summary>
    /// The progress shown in place of the editor area while slow project work runs, or
    /// <see langword="null"/> while the editor area is available.
    /// </summary>
    internal IReadOnlyReactiveProperty<ProjectLifecycleProgress?> LifecycleProgress { get; }

    private static ProjectLifecycleProgress? CreateLifecycleProgress(ProjectLifecycleActivity activity)
    {
        return activity switch
        {
            ProjectLifecycleActivity.CreatingProject => new ProjectLifecycleProgress(
                MessageStrings.CreatingProject,
                MessageStrings.CreatingProjectWithVersionControlMessage),
            ProjectLifecycleActivity.ClosingProject => new ProjectLifecycleProgress(
                MessageStrings.ClosingProject,
                MessageStrings.ClosingProjectWithVersionControlMessage),
            ProjectLifecycleActivity.EnablingVersionControl => new ProjectLifecycleProgress(
                MessageStrings.EnablingVersionControl,
                MessageStrings.EnablingVersionControlMessage),
            ProjectLifecycleActivity.CommittingVersion => new ProjectLifecycleProgress(
                Strings.VersionControl, Strings.VersionControl_Committing),
            ProjectLifecycleActivity.RestoringVersion => new ProjectLifecycleProgress(
                Strings.VersionControl, Strings.VersionControl_Restoring),
            ProjectLifecycleActivity.SwitchingBranch => new ProjectLifecycleProgress(
                Strings.VersionControl, Strings.VersionControl_SwitchingBranch),
            ProjectLifecycleActivity.ApplyingPull => new ProjectLifecycleProgress(
                Strings.VersionControl, Strings.VersionControl_ApplyingPull),
            _ => null,
        };
    }

    private Task OnProjectClosingAsync(
        ProjectService.ProjectCloseContext closeContext,
        CancellationToken _)
    {
        return QueueOperationAsync(async () =>
        {
            Project project = closeContext.Session.Project;
            bool saveChanges = closeContext.CloseIntent == ProjectService.ProjectCloseIntent.SaveChanges
                               && !ReferenceEquals(closeContext.PreparedEditorService, _editorService);
            if (!ReferenceEquals(_projectService.CurrentProject.Value, project))
            {
                // A session the editor does not show keeps its tabs aside, and only closing it releases them.
                if (closeContext.Kind == ProjectService.ProjectCloseKind.Close)
                    await DispatchAsync(() => CloseInactiveTabsAsync(closeContext, saveChanges));
                return;
            }

            CoreObject? selectedObject = _editorService.SelectedTabItem.Value?.Context.Value?.Object;
            // A scene removed from the project can still have its tab open, and the project cannot open on it.
            if (selectedObject is ProjectItem selectedItem && project.Items.Contains(selectedItem))
                ProjectViewState.SaveSelectedScene(project, selectedItem);

            if (closeContext.Kind == ProjectService.ProjectCloseKind.Deactivate)
            {
                // The tabs, with their edits and history, wait for the session to become active again. Nothing is
                // saved: the session is still loaded.
                await DispatchAsync(async () =>
                {
                    await _editorService.PutTabsAsideAsync(project);
                    UnsubscribeFromProject(project);
                });
                closeContext.RegisterCompletion(deactivated =>
                    RestoreAfterAbortedDeactivationAsync(project, deactivated));
                return;
            }

            // Failed activation may close the target too. Keep each model's selection
            // separately without retaining projects after a successful switch.
            _closedSelections.Remove(project);
            _closedSelections.Add(project, new ClosedProjectSelection(selectedObject));
            closeContext.RegisterCompletion(projectClosed =>
                RestoreAfterAbortedCloseAsync(project, selectedObject, projectClosed));

            await DispatchAsync(() => OnProjectChangedAsync(null, project, saveChanges));
        });
    }

    private Task OnProjectOpenedAsync(Project project)
    {
        return QueueOperationAsync(async () =>
        {
            // A session that was active before shows the tabs it had, as it had them.
            if (_editorService.TryTakeInactiveTabs(project, out EditorService.InactiveTabSet? inactiveTabs))
            {
                await DispatchAsync(() => RestoreInactiveTabsAsync(project, inactiveTabs));
                return;
            }

            await DispatchAsync(() => OnProjectChangedAsync(project, null, saveChanges: true));
            if (_closedSelections.TryGetValue(project, out ClosedProjectSelection? selection))
                _closedSelections.Remove(project);

            await DispatchAsync(() =>
            {
                // A model that opens again after a failed close or switch shows what it showed. A project read
                // from its file opens on the scene it showed when it last closed.
                CoreObject? selectedObject = selection is not null
                    ? selection.SelectedObject
                    : ProjectViewState.FindSelectedScene(project);
                if (selectedObject is ProjectItem item && project.Items.Contains(item))
                    _editorService.ActivateTabItem(item);

                return Task.CompletedTask;
            });
        });
    }

    // Puts the tabs of a session back into the tab list. A tab whose scene left the project meanwhile is closed.
    private async Task RestoreInactiveTabsAsync(Project project, EditorService.InactiveTabSet tabs)
    {
        try
        {
            var stale = new List<EditorTabItem>();
            foreach (EditorTabItem item in tabs.Items)
            {
                // A tab disposed while it was aside cannot come back.
                if (item.Context.Value is null)
                    continue;

                if (item.Context.Value?.Object is ProjectItem projectItem && !project.Items.Contains(projectItem))
                    stale.Add(item);
                else
                    _editorService.TabItems.Add(item);
            }

            SubscribeToProject(project);
            if (tabs.Selected is { } selected
                && _editorService.TabItems.Contains(selected)
                && selected.Context.Value?.Object is { } selectedObject)
            {
                _editorService.ActivateTabItem(selectedObject);
            }
            else if (ProjectViewState.FindSelectedScene(project) is ProjectItem item
                     && project.Items.Contains(item))
            {
                _editorService.ActivateTabItem(item);
            }
            else if (_editorService.TabItems.FirstOrDefault()?.Context.Value?.Object is { } firstObject)
            {
                // The selected scene left the project while it was inactive; show a tab that is still there.
                _editorService.ActivateTabItem(firstObject);
            }

            await DisposeTabsAsync(stale);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore the tabs of {Project}.", SafeLocalPath(project.Uri));
            _editorService.AppServices.GetRequiredService<INotificationService>().ShowError(Strings.Project, MessageStrings.OperationFailed);
        }
    }

    // A deactivation that was vetoed or failed leaves the session active, so its tabs come back.
    private Task RestoreAfterAbortedDeactivationAsync(Project project, bool deactivated)
    {
        if (deactivated
            || !ReferenceEquals(_projectService.CurrentProject.Value, project)
            || ReferenceEquals(_subscribedProject, project))
        {
            return Task.CompletedTask;
        }

        return QueueOperationAsync(async () =>
        {
            if (_editorService.TryTakeInactiveTabs(project, out EditorService.InactiveTabSet? tabs))
                await DispatchAsync(() => RestoreInactiveTabsAsync(project, tabs));
        });
    }

    // Closing a session the editor does not show saves its tabs, unless the close discards them, and disposes them
    // once the close commits. A failed save vetoes the close; a close aborted later, by a cancellation or another
    // handler, keeps the tabs aside with their history.
    private async Task CloseInactiveTabsAsync(ProjectService.ProjectCloseContext closeContext, bool saveChanges)
    {
        Project project = closeContext.Session.Project;
        if (!_editorService.HasInactiveTabs(project))
            return;

        if (saveChanges
            && _editorService.TryGetInactiveTabs(project, out EditorService.InactiveTabSet? toSave)
            && !await _editorService.SaveSceneEditorsBeforeCloseAsync(toSave.Items))
        {
            throw new ProjectCloseAbortedException(MessageStrings.FileSaveException);
        }

        closeContext.RegisterCompletion(closed => closed
            ? QueueOperationAsync(() => DispatchAsync(async () =>
            {
                if (!_editorService.TryTakeInactiveTabs(project, out EditorService.InactiveTabSet? tabs))
                    return;

                _closedSelections.Remove(project);
                await DisposeTabsAsync(tabs.Items);
            }))
            : Task.CompletedTask);
    }

    private async Task DisposeTabsAsync(IEnumerable<EditorTabItem> items)
    {
        foreach (EditorTabItem item in items)
        {
            // Capture FilePath before DisposeAsync nulls out the underlying context.
            string? filePath = item.FilePath.Value;
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

    private sealed record ClosedProjectSelection(CoreObject? SelectedObject);

    internal sealed record ProjectLifecycleProgress(string Title, string Message);

    private Task RestoreAfterAbortedCloseAsync(
        Project project,
        CoreObject? selectedObject,
        bool projectClosed)
    {
        if (projectClosed || !ReferenceEquals(_projectService.CurrentProject.Value, project)
            || ReferenceEquals(_subscribedProject, project))
        {
            return Task.CompletedTask;
        }

        return QueueOperationAsync(async () =>
        {
            // A save veto leaves the original tabs published. Only rebuild tabs that were torn down.
            await DispatchAsync(() => OnProjectChangedAsync(project, null, saveChanges: false));
            if (selectedObject is ProjectItem selectedItem && project.Items.Contains(selectedItem))
            {
                await DispatchAsync(() =>
                {
                    _editorService.ActivateTabItem(selectedItem);
                    return Task.CompletedTask;
                });
            }
        });
    }

    private static async Task DispatchAsync(Func<Task> action)
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

    public IReactiveProperty<EditorTabItem?> SelectedTabItem => _editorService.SelectedTabItem;

    private async Task OnProjectChangedAsync(Project? @new, Project? old, bool saveChanges)
    {
        var oldItems = _editorService.TabItems.ToArray();
        if (saveChanges && !await _editorService.SaveSceneEditorsBeforeCloseAsync(oldItems))
            throw new ProjectCloseAbortedException(MessageStrings.FileSaveException);

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
                await DisposeTabsAsync(oldItems);
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
            _editorService.AppServices.GetRequiredService<INotificationService>().ShowError(Strings.Project, MessageStrings.OperationFailed);
        }
    }

    private void Project_Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        long generation;
        lock (_operationGate)
        {
            generation = _subscriptionGeneration;
        }

        // Capture the mutation's navigation policy before queueing: a background edit scope may
        // have ended by the time this collection notification reaches the editor host.
        bool activateAddedItems = _editorService.ActivateAddedProjectItems;
        _ = QueueOperationAsync(() => HandleProjectItemsChangedAsync(sender, e, generation, activateAddedItems));
    }

    private async Task HandleProjectItemsChangedAsync(
        object? sender,
        NotifyCollectionChangedEventArgs e,
        long generation,
        bool activateAddedItems)
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
                if (!activateAddedItems)
                    return;

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
            _editorService.AppServices.GetRequiredService<INotificationService>().ShowError(Strings.Project, MessageStrings.OperationFailed);
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
