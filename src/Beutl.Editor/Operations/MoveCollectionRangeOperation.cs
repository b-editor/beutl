using Beutl.Engine;

using Beutl.Editor.Infrastructure;

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
            ApplyToCoreProperty(Object, coreProperty);
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

    private void ApplyToCoreProperty(CoreObject obj, CoreProperty coreProperty)
    {
        if (obj.GetValue(coreProperty) is not IList<T> list)
        {
            throw new InvalidOperationException($"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        var items = new T[Count];
        for (int i = 0; i < Count; i++)
        {
            items[i] = list[OldIndex]!;
            list.RemoveAt(OldIndex);
        }

        for (int i = 0; i < Count; i++)
        {
            list.Insert(NewIndex + i, items[i]);
        }
    }

    public override void Revert(OperationExecutionContext context)
    {
        var type = Object.GetType();
        var name = PropertyPathHelper.GetPropertyNameFromPath(PropertyPath);
        var coreProperty = PropertyRegistry.FindRegistered(type, name);

        if (coreProperty != null)
        {
            RevertToCoreProperty(Object, coreProperty);
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

    private void RevertToCoreProperty(CoreObject obj, CoreProperty coreProperty)
    {
        if (obj.GetValue(coreProperty) is not IList<T> list)
        {
            throw new InvalidOperationException($"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        var items = new T[Count];
        for (int i = 0; i < Count; i++)
        {
            items[i] = list[NewIndex]!;
            list.RemoveAt(NewIndex);
        }

        for (int i = 0; i < Count; i++)
        {
            list.Insert(OldIndex + i, items[i]);
        }
    }
}
