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
        var (objectsToSave, detachedCollectionItems) = CollectSaveObjects(operations);
        SaveObjects(objectsToSave, detachedCollectionItems);
    }

    // Capture the complete batch before yielding. The writer only receives serialized JSON and
    // paths, so edits made while I/O is pending belong to the next save, not half of this one.
    public async Task AutoSaveAsync(IEnumerable<ChangeOperation> operations, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ChangeOperation[] batch = operations.ToArray();
        var (objectsToSave, detachedCollectionItems) = CollectSaveObjects(batch);
        int reported = -1;
        Exception? captureError = null;
        var progressSync = new object();
        bool hasDocumentContext = SynchronizationContext.Current != null;
        IProgress<(int Percentage, Exception? Error)> progress =
            new Progress<(int Percentage, Exception? Error)>(value => Report(value.Percentage, value.Error));
        Report(0, null);
        var writes = new List<Action>();
        var pendingFiles = new Dictionary<string, bool>(StringComparer.Ordinal);
        var deletedFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (CoreObject obj in objectsToSave.OrderBy(obj => obj is Scene))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (IsDetachedFromProject(obj, detachedCollectionItems))
                {
                    if (obj.SuppressedStorageSource is null && obj.Uri is { IsFile: true } uri)
                    {
                        string path = uri.LocalPath;
                        writes.Add(() =>
                        {
                            File.Delete(path);
                            deletedFiles.Add(path);
                        });
                        pendingFiles[path] = false;
                    }
                }
                else
                {
                    writes.Add(CoreSerializer.PrepareStoreToUri(obj, pendingFiles, obj is Scene
                        ? (json, uri) => Scene.RemoveDeletedElementExclusions(json, uri, deletedFiles)
                        : null));
                    pendingFiles[obj.Uri!.LocalPath] = true;
                }
            }
            catch (Exception ex)
            {
                captureError = ex;
                ReportError(ex);
            }
        }

        var errors = new List<Exception>();
        try
        {
            await Task.Run(() =>
            {
                int completed = 0, lastPercentage = 0;
                foreach (Action write in writes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { write(); }
                    catch (Exception ex) { errors.Add(ex); }
                    int percentage = (int)((long)++completed * 100 / writes.Count);
                    // At most one notification per percentage; completion is published after awaiting I/O.
                    if (percentage < 100 && percentage != lastPercentage)
                    {
                        lastPercentage = percentage;
                        Exception? error = errors.LastOrDefault() ?? captureError;
                        if (hasDocumentContext) progress.Report((percentage, error));
                        else Report(percentage, error);
                    }
                }
            }, cancellationToken);
        }
        finally
        {
            foreach (Exception error in errors) ReportError(error);
        }
        Report(100, errors.LastOrDefault() ?? captureError);

        void Report(int percentage, Exception? error)
        {
            lock (progressSync)
            {
                // A posted update may arrive after the completion continuation or after disposal.
                if (_isDisposed || percentage <= reported) return;
                reported = percentage;
                _saveProgress.OnNext(new(batch, percentage, error));
            }
        }
    }

    private static (HashSet<CoreObject> Objects, HashSet<CoreObject> Detached) CollectSaveObjects(
        IEnumerable<ChangeOperation> operations)
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
                ReportError(ex);
            }
        }
    }

    private void ReportError(Exception error)
    {
        _logger.LogError(error, "An exception occurred while auto-saving the file.");
        if (!_isDisposed) _saveError.OnNext(error);
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
        _saveProgress.Dispose();
        _isDisposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
