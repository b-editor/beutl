using System.Reactive.Linq;
using System.Reactive.Subjects;
using Beutl.Editor.Operations;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor;

public readonly record struct AutoSaveProgress(IReadOnlyCollection<ChangeOperation> Operations, int Percentage, Exception? Error);

public sealed class AutoSaveService : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<AutoSaveService>();
    private readonly Subject<Exception> _saveError = new();
    private readonly Subject<AutoSaveProgress> _saveProgress = new();
    private bool _isDisposed;

    public IObservable<Exception> SaveError => _saveError.AsObservable();
    public IObservable<AutoSaveProgress> SaveProgress => _saveProgress.AsObservable();

    public void AutoSave(IEnumerable<ChangeOperation> operations)
    {
        ThrowIfDisposed();
        var (objects, detached) = Collect(operations);
        SaveObjects(objects, detached);
    }

    public async Task AutoSaveAsync(IEnumerable<ChangeOperation> operations, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ChangeOperation[] batch = operations.ToArray();
        var (objects, detached) = Collect(batch);
        // Await each file before capturing the scene's include patterns. All live-object reads,
        // serializers and error notifications stay on the caller's document context.
        int completed = 0, reported = -1;
        Exception? error = null;
        Report(0);
        foreach (CoreObject obj in objects.OrderBy(obj => obj is Scene))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (IsDetachedFromProject(obj, detached))
                {
                    string? path = GetDeletedFilePath(obj);
                    if (path != null) await Task.Run(() => { if (File.Exists(path)) File.Delete(path); });
                }
                else
                {
                    await CoreSerializer.StoreToUriAsync(obj, obj.Uri!);
                }
            }
            catch (Exception ex)
            {
                error = ex;
                _logger.LogError(ex, "An exception occurred while auto-saving the file.");
                if (!_isDisposed) _saveError.OnNext(ex);
            }
            Report((int)((long)++completed * 100 / objects.Count));
        }
        Report(100);

        void Report(int value)
        {
            if (_isDisposed || reported == value) return;
            reported = value;
            _saveProgress.OnNext(new(batch, value, error));
        }
    }

    private static (HashSet<CoreObject> Objects, HashSet<CoreObject> Detached) Collect(IEnumerable<ChangeOperation> operations)
    {
        HashSet<CoreObject> objectsToSave = [];
        HashSet<CoreObject> detachedCollectionItems = [];

        foreach (ChangeOperation operation in operations)
        {
            CollectObjectsToSave(operation, objectsToSave);
            if (operation is ICollectionChangeOperation collection)
            {
                foreach (CoreObject item in collection.Items.OfType<CoreObject>())
                {
                    if (item is IHierarchical { HierarchicalParent: null } and not (Project or ProjectItem))
                        detachedCollectionItems.Add(item);
                }
            }
        }

        return (objectsToSave, detachedCollectionItems);
    }

    // Internal as a test seam: tests save chosen objects without going through AutoSave.
    internal void SaveObjects(IEnumerable<CoreObject> objectsToSave) => SaveObjects(objectsToSave, null);

    private void SaveObjects(IEnumerable<CoreObject> objectsToSave, IReadOnlySet<CoreObject>? detachedCollectionItems)
    {
        ThrowIfDisposed();

        // A scene lists the element files it finds on disk, so a new element's file is written first.
        foreach (CoreObject obj in objectsToSave.OrderBy(obj => obj is Scene))
        {
            try
            {
                if (IsDetachedFromProject(obj, detachedCollectionItems))
                {
                    DeleteStoredFile(obj);
                    continue;
                }

                _logger.LogTrace(
                    "Auto-saving object ({TypeName}, {ObjectId}).",
                    TypeFormat.ToString(obj.GetType()),
                    obj.Id.Value);
                CoreSerializer.StoreToUri(obj, obj.Uri!, CoreSerializationMode.Write);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An exception occurred while auto-saving the file.");
                _saveError.OnNext(ex);
            }
        }
    }

    private static bool IsDetachedFromProject(CoreObject obj, IReadOnlySet<CoreObject>? detachedCollectionItems)
        => obj is IHierarchical { HierarchicalParent: null }
           && (obj is Element || detachedCollectionItems?.Contains(obj) == true);

    private static void DeleteStoredFile(CoreObject obj)
    {
        if (GetDeletedFilePath(obj) is { } path && File.Exists(path)) File.Delete(path);
    }

    private static string? GetDeletedFilePath(CoreObject obj)
        => obj.SuppressedStorageSource is null && obj.Uri is { IsFile: true } uri ? uri.LocalPath : null;

    internal static void CollectObjectsToSave(ChangeOperation operation, HashSet<CoreObject> objectsToSave)
    {
        CoreObject? obj = null;

        if (operation is IUpdatePropertyValueOperation updateOp)
        {
            obj = updateOp.Object;
        }
        else if (operation is ICollectionChangeOperation collectionOp)
        {
            obj = collectionOp.Object;

            foreach (CoreObject? item in collectionOp.Items.OfType<CoreObject>())
            {
                AddObjectWithAncestors(item, objectsToSave);
            }
        }
        else if (operation is UpdateSplineEasingOperation splineOp)
        {
            obj = splineOp.Parent;
        }
        else if (operation is UpdateNodeMemberOperation nodeOp)
        {
            obj = nodeOp.NodeMember as CoreObject;
        }

        if (obj != null)
        {
            AddObjectWithAncestors(obj, objectsToSave);
        }
    }

    internal static void AddObjectWithAncestors(CoreObject obj, HashSet<CoreObject> objectsToSave)
    {
        if (obj.Uri != null)
        {
            objectsToSave.Add(obj);
        }

        if (obj is IHierarchical hierarchical)
        {
            foreach (CoreObject ancestor in hierarchical.EnumerateAncestors<CoreObject>().Where(a => a.Uri != null))
            {
                objectsToSave.Add(ancestor);
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        _saveError.Dispose();
        _saveProgress.Dispose();
        _isDisposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
