using Beutl.Engine;
using Beutl.Editor.Infrastructure;
using Beutl.NodeTree;

namespace Beutl.Editor.Operations;

public sealed class InsertCollectionItemOperation<T> : ChangeOperation, IPropertyPathProvider,
    ICollectionChangeOperation
{
    public required CoreObject Object { get; set; }

    public required string PropertyPath { get; set; }

    public required T Item { get; set; }

    public required int Index { get; set; }

    IEnumerable<object?> ICollectionChangeOperation.Items => [Item];

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
                                 ?? throw new InvalidOperationException(
                                     $"Engine property {PropertyPath} not found on type {type.FullName}.");

            if (engineProperty is not IListProperty<T> listProperty)
            {
                throw new InvalidOperationException(
                    $"Engine property {PropertyPath} is not a list on type {type.FullName}.");
            }

            ApplyToEngineProperty(listProperty);
        }
    }

    private void ApplyToEngineProperty(IListProperty<T> listProperty)
    {
        if (Index < 0 || Index > listProperty.Count)
        {
            listProperty.Add(Item);
        }
        else
        {
            listProperty.Insert(Index, Item);
        }
    }

    private void ApplyTo(object obj, object? list)
    {
        if (list is not IList<T> list2)
        {
            throw new InvalidOperationException(
                $"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        if (Index < 0 || Index > list2.Count)
        {
            list2.Add(Item);
        }
        else
        {
            list2.Insert(Index, Item);
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
                                 ?? throw new InvalidOperationException(
                                     $"Engine property {PropertyPath} not found on type {type.FullName}.");

            if (engineProperty is not IListProperty<T> listProperty)
            {
                throw new InvalidOperationException(
                    $"Engine property {PropertyPath} is not a list on type {type.FullName}.");
            }

            RevertToEngineProperty(listProperty);
        }
    }

    private void RevertToEngineProperty(IListProperty<T> listProperty)
    {
        listProperty.RemoveAt(Index);
    }

    private void RevertTo(object obj, object? list)
    {
        if (list is not IList<T> list2)
        {
            throw new InvalidOperationException(
                $"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        list2.RemoveAt(Index);
    }
}
