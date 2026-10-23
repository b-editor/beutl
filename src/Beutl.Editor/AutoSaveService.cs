using System.Reactive.Linq;
using System.Reactive.Subjects;
using Beutl.Editor.Operations;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor;

public sealed class AutoSaveService : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<AutoSaveService>();
    private readonly Subject<Exception> _saveError = new();
    private bool _isDisposed;

    public IObservable<Exception> SaveError => _saveError.AsObservable();

    public void AutoSave(IEnumerable<ChangeOperation> operations)
    {
        ThrowIfDisposed();

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

        SaveObjects(objectsToSave, detachedCollectionItems);
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
        if (obj.SuppressedStorageSource is null && obj.Uri is { IsFile: true } uri)
        {
            var path = uri.LocalPath;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

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
        _isDisposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
