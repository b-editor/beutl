using Beutl.Engine;

using Beutl.Editor.Infrastructure;

namespace Beutl.Editor.Operations;

public sealed class MoveCollectionItemOperation<T> : ChangeOperation, IPropertyPathProvider
{
    public required CoreObject Object { get; set; }

    public required string PropertyPath { get; set; }

    public required int OldIndex { get; set; }

    public required int NewIndex { get; set; }

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
        listProperty.Move(OldIndex, NewIndex);
    }

    private void ApplyToCoreProperty(CoreObject obj, CoreProperty coreProperty)
    {
        if (obj.GetValue(coreProperty) is not IList<T> list)
        {
            throw new InvalidOperationException($"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        var item = list[OldIndex];
        list.RemoveAt(OldIndex);
        list.Insert(NewIndex, item);
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
        listProperty.Move(NewIndex, OldIndex);
    }

    private void RevertToCoreProperty(CoreObject obj, CoreProperty coreProperty)
    {
        if (obj.GetValue(coreProperty) is not IList<T> list)
        {
            throw new InvalidOperationException($"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        var item = list[NewIndex];
        list.RemoveAt(NewIndex);
        list.Insert(OldIndex, item);
    }
}
