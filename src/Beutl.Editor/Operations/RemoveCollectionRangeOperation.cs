using Beutl.Engine;
using Beutl.Editor.Infrastructure;

namespace Beutl.Editor.Operations;

public sealed class RemoveCollectionRangeOperation<T> : ChangeOperation, IPropertyPathProvider, ICollectionChangeOperation
{
    public required CoreObject Object { get; set; }

    public required string PropertyPath { get; set; }

    public required int Index { get; set; }

    public required T[] Items { get; set; }

    IEnumerable<object?> ICollectionChangeOperation.Items => Items.Cast<object?>();

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
        listProperty.RemoveRange(Index, Items.Length);
    }

    private void ApplyToCoreProperty(CoreObject obj, CoreProperty coreProperty)
    {
        if (obj.GetValue(coreProperty) is not IList<T> list)
        {
            throw new InvalidOperationException($"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        for (int i = 0; i < Items.Length; i++)
        {
            list.RemoveAt(Index);
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
        for (int i = 0; i < Items.Length; i++)
        {
            listProperty.Insert(Index + i, Items[i]);
        }
    }

    private void RevertToCoreProperty(CoreObject obj, CoreProperty coreProperty)
    {
        if (obj.GetValue(coreProperty) is not IList<T> list)
        {
            throw new InvalidOperationException($"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        for (int i = 0; i < Items.Length; i++)
        {
            list.Insert(Index + i, Items[i]);
        }
    }
}
