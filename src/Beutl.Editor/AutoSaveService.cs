using System.Reactive.Linq;
using System.Reactive.Subjects;
using Beutl.Editor.Operations;
using Beutl.Logging;
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

        // ChangeOperationから保存対象のオブジェクトを収集
        foreach (ChangeOperation operation in operations)
        {
            CollectObjectsToSave(operation, objectsToSave);
        }

        SaveObjects(objectsToSave);
    }

    public void SaveObjects(IEnumerable<CoreObject> objectsToSave)
    {
        ThrowIfDisposed();

        // 各オブジェクトを保存
        foreach (CoreObject obj in objectsToSave)
        {
            try
            {
                if (obj is IHierarchical hierarchical && hierarchical.HierarchicalRoot == null) continue;

                _logger.LogTrace(
                    "Auto-saving object ({TypeName}, {ObjectId}).",
                    TypeFormat.ToString(obj.GetType()),
                    obj.Id);
                CoreSerializer.StoreToUri(obj, obj.Uri!, CoreSerializationMode.Write);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An exception occurred while auto-saving the file.");
                _saveError.OnNext(ex);
            }
        }
    }

    public static void CollectObjectsToSave(ChangeOperation operation, HashSet<CoreObject> objectsToSave)
    {
        CoreObject? obj = null;

        if (operation is IUpdatePropertyValueOperation updateOp)
        {
            obj = updateOp.Object;
        }
        else if (operation is ICollectionChangeOperation collectionOp)
        {
            obj = collectionOp.Object;

            // コレクション内のアイテムも保存対象に追加
            foreach (CoreObject? item in collectionOp.Items.OfType<CoreObject>())
            {
                AddObjectWithAncestors(item, objectsToSave);
            }
        }
        else if (operation is UpdateSplineEasingOperation splineOp)
        {
            obj = splineOp.Parent;
        }
        else if (operation is UpdateNodeItemOperation nodeOp)
        {
            obj = nodeOp.NodeItem as CoreObject;
        }

        if (obj != null)
        {
            AddObjectWithAncestors(obj, objectsToSave);
        }
    }

    public static void AddObjectWithAncestors(CoreObject obj, HashSet<CoreObject> objectsToSave)
    {
        // オブジェクト自体にUriがあれば追加
        if (obj.Uri != null)
        {
            objectsToSave.Add(obj);
        }

        // 親要素を辿ってUriを持つオブジェクトを探す
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
