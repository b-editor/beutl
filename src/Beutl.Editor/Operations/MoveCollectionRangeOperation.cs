using Beutl.Engine;

using Beutl.Editor.Infrastructure;
using Beutl.NodeTree;

namespace Beutl.Editor.Operations;

public sealed class MoveCollectionRangeOperation<T> : ChangeOperation, IPropertyPathProvider, ICollectionChangeOperation
{
    public required CoreObject Object { get; set; }

    public required string PropertyPath { get; set; }

    public required int OldIndex { get; set; }

    public required int NewIndex { get; set; }

    public required int Count { get; set; }

    // Move操作ではアイテムを直接保持しないため、空のコレクションを返す
    IEnumerable<object?> ICollectionChangeOperation.Items => [];

    public override void Apply(OperationExecutionContext context)
    {
        var type = Object.GetType();
        var name = PropertyPathHelper.GetPropertyNameFromPath(PropertyPath);
        var coreProperty = PropertyRegistry.FindRegistered(type, name);

        if (coreProperty != null)
        {
            ApplyTo(Object, Object.GetValue(coreProperty));
            return;
        }

        if (Object is INodeItem nodeItem && name == "Property")
        {
            ApplyTo(nodeItem, nodeItem.Property?.GetValue());
            return;
        }

        if (Object is EngineObject engineObj)
        {
            var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == name)
                ?? throw new InvalidOperationException($"Engine property {PropertyPath} not found on type {type.FullName}.");

            if (engineProperty is not IListProperty<T> listProperty)
            {
                throw new InvalidOperationException($"Engine property {PropertyPath} is not a list on type {type.FullName}.");
            }

            ApplyToEngineProperty(listProperty);
        }
    }

    private void ApplyToEngineProperty(IListProperty<T> listProperty)
    {
        listProperty.MoveRange(OldIndex, NewIndex, Count);
    }

    private void ApplyTo(object obj, object? list)
    {
        if (list is not IList<T> list2)
        {
            throw new InvalidOperationException(
                $"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        var items = new T[Count];
        for (int i = 0; i < Count; i++)
        {
            items[i] = list2[OldIndex]!;
            list2.RemoveAt(OldIndex);
        }

        for (int i = 0; i < Count; i++)
        {
            list2.Insert(NewIndex + i, items[i]);
        }
    }

    public override void Revert(OperationExecutionContext context)
    {
        var type = Object.GetType();
        var name = PropertyPathHelper.GetPropertyNameFromPath(PropertyPath);
        var coreProperty = PropertyRegistry.FindRegistered(type, name);

        if (coreProperty != null)
        {
            RevertTo(Object, Object.GetValue(coreProperty));
            return;
        }

        if (Object is INodeItem nodeItem && name == "Property")
        {
            RevertTo(nodeItem, nodeItem.Property?.GetValue());
            return;
        }

        if (Object is EngineObject engineObj)
        {
            var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == name)
                ?? throw new InvalidOperationException($"Engine property {PropertyPath} not found on type {type.FullName}.");

            if (engineProperty is not IListProperty<T> listProperty)
            {
                throw new InvalidOperationException($"Engine property {PropertyPath} is not a list on type {type.FullName}.");
            }

            RevertToEngineProperty(listProperty);
        }
    }

    private void RevertToEngineProperty(IListProperty<T> listProperty)
    {
        listProperty.MoveRange(NewIndex, OldIndex, Count);
    }

    private void RevertTo(object obj, object? list)
    {
        if (list is not IList<T> list2)
        {
            throw new InvalidOperationException(
                $"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        var items = new T[Count];
        for (int i = 0; i < Count; i++)
        {
            items[i] = list2[NewIndex]!;
            list2.RemoveAt(NewIndex);
        }

        for (int i = 0; i < Count; i++)
        {
            list2.Insert(OldIndex + i, items[i]);
        }
    }
}
