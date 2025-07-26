using System.Collections.Specialized;
using System.Reactive.Linq;
using Beutl.Collections;
using Beutl.ProjectSystem;
using Beutl.Synchronization.Core;
using Beutl.Synchronization.Extensions;
using Microsoft.Extensions.Logging;

namespace Beutl.Synchronization.Orchestrators;

/// <summary>
/// Orchestrates synchronization for an entire project hierarchy
/// </summary>
public class ProjectSyncOrchestrator : IDisposable
{
    private readonly ISyncManager _syncManager;
    private readonly ILogger<ProjectSyncOrchestrator> _logger;
    private readonly HashSet<CoreObject> _trackedObjects = new();
    private readonly List<IDisposable> _subscriptions = new();
    
    private Project? _currentProject;
    private bool _disposed;

    public ProjectSyncOrchestrator(ISyncManager syncManager, ILogger<ProjectSyncOrchestrator>? logger = null)
    {
        _syncManager = syncManager ?? throw new ArgumentNullException(nameof(syncManager));
        _logger = logger ?? CreateDefaultLogger();
    }

    /// <summary>
    /// Currently synchronized project
    /// </summary>
    public Project? CurrentProject => _currentProject;

    /// <summary>
    /// Number of objects currently being synchronized
    /// </summary>
    public int TrackedObjectCount => _trackedObjects.Count;

    /// <summary>
    /// Start synchronizing a project and all its contents
    /// </summary>
    /// <param name="project">Project to synchronize</param>
    /// <param name="sourceId">Source identifier for changes</param>
    public async Task SyncProjectAsync(Project project, string? sourceId = null)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));
        ThrowIfDisposed();

        // Stop syncing current project if different
        if (_currentProject != null && _currentProject != project)
        {
            await StopSyncAsync();
        }

        if (_currentProject == project)
        {
            _logger.LogDebug("Project {ProjectId} is already being synchronized", project.Id);
            return;
        }

        _logger.LogInformation("Starting synchronization for project {ProjectId} ({ProjectName})",
            project.Id, project.Name);

        _currentProject = project;

        try
        {
            // Enable sync for the project itself
            await EnableSyncForObjectAsync(project, sourceId);

            // Sync all project items (scenes)
            foreach (ProjectItem item in project.Items)
            {
                await SyncProjectItemAsync(item, sourceId);
            }

            // Watch for new project items being added/removed
            var projectItemsSubscription = Observable
                .FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                    handler => project.Items.CollectionChanged += handler,
                    handler => project.Items.CollectionChanged -= handler)
                .Subscribe(evt => OnProjectItemsChanged(evt.EventArgs, sourceId));

            _subscriptions.Add(projectItemsSubscription);

            _logger.LogInformation("Successfully started synchronization for project {ProjectId}, tracking {ObjectCount} objects",
                project.Id, _trackedObjects.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start synchronization for project {ProjectId}", project.Id);
            await StopSyncAsync();
            throw;
        }
    }

    /// <summary>
    /// Stop synchronizing the current project
    /// </summary>
    public async Task StopSyncAsync()
    {
        ThrowIfDisposed();

        if (_currentProject == null)
        {
            _logger.LogDebug("No project is currently being synchronized");
            return;
        }

        _logger.LogInformation("Stopping synchronization for project {ProjectId}", _currentProject.Id);

        try
        {
            // Disable sync for all tracked objects
            foreach (var obj in _trackedObjects.ToArray())
            {
                obj.DisableSync();
            }

            _trackedObjects.Clear();

            // Dispose all subscriptions
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
            _subscriptions.Clear();

            var projectId = _currentProject.Id;
            _currentProject = null;

            _logger.LogInformation("Successfully stopped synchronization for project {ProjectId}", projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping project synchronization");
            throw;
        }
    }

    /// <summary>
    /// Check if an object is being synchronized
    /// </summary>
    /// <param name="obj">Object to check</param>
    /// <returns>True if synchronized</returns>
    public bool IsObjectSynchronized(CoreObject obj)
    {
        ThrowIfDisposed();
        return _trackedObjects.Contains(obj);
    }

    /// <summary>
    /// Manually add an object to synchronization
    /// </summary>
    /// <param name="obj">Object to synchronize</param>
    /// <param name="sourceId">Source identifier</param>
    public async Task EnableSyncForObjectAsync(CoreObject obj, string? sourceId = null)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        ThrowIfDisposed();

        if (_trackedObjects.Contains(obj))
        {
            _logger.LogTrace("Object {ObjectId} is already synchronized", obj.Id);
            return;
        }

        try
        {
            obj.EnableSync(_syncManager, sourceId);
            _trackedObjects.Add(obj);

            _logger.LogTrace("Enabled synchronization for object {ObjectId} of type {ObjectType}",
                obj.Id, obj.GetType().Name);

            // If it's a hierarchical object, sync its children too
            if (obj is Hierarchical hierarchical)
            {
                await SyncHierarchyAsync(hierarchical, sourceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable synchronization for object {ObjectId} of type {ObjectType}",
                obj.Id, obj.GetType().Name);
            throw;
        }
    }

    /// <summary>
    /// Manually remove an object from synchronization
    /// </summary>
    /// <param name="obj">Object to stop synchronizing</param>
    public void DisableSyncForObject(CoreObject obj)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        ThrowIfDisposed();

        if (!_trackedObjects.Contains(obj))
        {
            _logger.LogTrace("Object {ObjectId} is not synchronized", obj.Id);
            return;
        }

        try
        {
            obj.DisableSync();
            _trackedObjects.Remove(obj);

            _logger.LogTrace("Disabled synchronization for object {ObjectId} of type {ObjectType}",
                obj.Id, obj.GetType().Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling synchronization for object {ObjectId}", obj.Id);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogDebug("Disposing ProjectSyncOrchestrator");

        try
        {
            StopSyncAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping synchronization during disposal");
        }

        _disposed = true;
        _logger.LogDebug("ProjectSyncOrchestrator disposed");
    }

    private async Task SyncProjectItemAsync(ProjectItem item, string? sourceId)
    {
        try
        {
            await EnableSyncForObjectAsync(item, sourceId);

            // If it's a scene, sync all its elements
            if (item is Scene scene)
            {
                await SyncSceneAsync(scene, sourceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync project item {ItemId} of type {ItemType}",
                item.Id, item.GetType().Name);
        }
    }

    private async Task SyncSceneAsync(Scene scene, string? sourceId)
    {
        try
        {
            // Sync all elements in the scene
            foreach (var element in scene.Children.OfType<Element>())
            {
                await EnableSyncForObjectAsync(element, sourceId);
            }

            // Watch for elements being added/removed
            var elementsSubscription = Observable
                .FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                    handler => scene.Children.CollectionChanged += handler,
                    handler => scene.Children.CollectionChanged -= handler)
                .Subscribe(evt => OnSceneElementsChanged(evt.EventArgs, sourceId));

            _subscriptions.Add(elementsSubscription);

            _logger.LogDebug("Synchronized scene {SceneId} with {ElementCount} elements",
                scene.Id, scene.Children.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync scene {SceneId}", scene.Id);
        }
    }

    private async Task SyncHierarchyAsync(Hierarchical hierarchical, string? sourceId)
    {
        try
        {
            var hierarchicalInterface = (IHierarchical)hierarchical;
            foreach (var child in hierarchicalInterface.HierarchicalChildren.OfType<CoreObject>())
            {
                await EnableSyncForObjectAsync(child, sourceId);
            }

            // Watch for hierarchical children changes if it's an ICoreList
            if (hierarchicalInterface.HierarchicalChildren is ICoreList<IHierarchical> coreList)
            {
                var hierarchySubscription = Observable
                    .FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                        handler => coreList.CollectionChanged += handler,
                        handler => coreList.CollectionChanged -= handler)
                    .Subscribe(evt => OnHierarchyChanged(evt.EventArgs, sourceId));

                _subscriptions.Add(hierarchySubscription);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync hierarchy for {ObjectId}", hierarchical.Id);
        }
    }

    private void OnProjectItemsChanged(NotifyCollectionChangedEventArgs e, string? sourceId)
    {
        try
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null)
                    {
                        foreach (ProjectItem item in e.NewItems)
                        {
                            _ = Task.Run(() => SyncProjectItemAsync(item, sourceId));
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                    {
                        foreach (ProjectItem item in e.OldItems)
                        {
                            DisableSyncForObject(item);
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Reset:
                    // Re-sync all current items
                    if (_currentProject != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            foreach (ProjectItem item in _currentProject.Items)
                            {
                                await SyncProjectItemAsync(item, sourceId);
                            }
                        });
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling project items change");
        }
    }

    private void OnSceneElementsChanged(NotifyCollectionChangedEventArgs e, string? sourceId)
    {
        try
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null)
                    {
                        foreach (var item in e.NewItems.OfType<CoreObject>())
                        {
                            _ = Task.Run(() => EnableSyncForObjectAsync(item, sourceId));
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                    {
                        foreach (var item in e.OldItems.OfType<CoreObject>())
                        {
                            DisableSyncForObject(item);
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling scene elements change");
        }
    }

    private void OnHierarchyChanged(NotifyCollectionChangedEventArgs e, string? sourceId)
    {
        try
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null)
                    {
                        foreach (var item in e.NewItems.OfType<CoreObject>())
                        {
                            _ = Task.Run(() => EnableSyncForObjectAsync(item, sourceId));
                        }
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                    {
                        foreach (var item in e.OldItems.OfType<CoreObject>())
                        {
                            DisableSyncForObject(item);
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling hierarchy change");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ProjectSyncOrchestrator));
        }
    }

    private static ILogger<ProjectSyncOrchestrator> CreateDefaultLogger()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        return loggerFactory.CreateLogger<ProjectSyncOrchestrator>();
    }
}