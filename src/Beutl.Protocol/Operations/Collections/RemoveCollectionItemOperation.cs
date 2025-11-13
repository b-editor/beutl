using System.Collections;
using Beutl.Engine;

namespace Beutl.Protocol.Operations.Collections;

public sealed class RemoveCollectionItemOperation : SyncOperation
{
    public required Guid ObjectId { get; set; }

    public required string PropertyPath { get; set; }

    public required Guid ItemId { get; set; }

    public override void Apply(OperationExecutionContext context)
    {
        var obj = context.FindObject(ObjectId)
            ?? throw new InvalidOperationException($"Object with ID {ObjectId} not found.");

        var name = PropertyPathHelper.GetPropertyNameFromPath(PropertyPath);
        var coreProperty = PropertyRegistry.FindRegistered(obj.GetType(), name);

        if (coreProperty != null)
        {
            ApplyToCoreProperty(obj, coreProperty);
            return;
        }

        if (obj is EngineObject engineObj)
        {
            var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == name)
                ?? throw new InvalidOperationException($"Engine property {PropertyPath} not found on type {obj.GetType().FullName}.");

            if (engineProperty is not IListProperty listProperty)
            {
                throw new InvalidOperationException($"Engine property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
            }

            ApplyToEngineProperty(listProperty);
        }
    }

    private void ApplyToEngineProperty(IListProperty listProperty)
    {
        var item = listProperty.OfType<EngineObject>().FirstOrDefault(x => x.Id == ItemId)
            ?? throw new InvalidOperationException($"Item with ID {ItemId} not found in property {PropertyPath}.");

        listProperty.Remove(item);
    }

    private void ApplyToCoreProperty(ICoreObject obj, CoreProperty coreProperty)
    {
        if (obj.GetValue(coreProperty) is not IList list)
        {
            throw new InvalidOperationException($"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        var item = list.OfType<CoreObject>().FirstOrDefault(x => x.Id == ItemId)
            ?? throw new InvalidOperationException($"Item with ID {ItemId} not found in property {PropertyPath}.");

        list.Remove(item);
    }
}
