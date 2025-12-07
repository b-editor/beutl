using System.Collections;
using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Serialization;

using Beutl.Editor.Infrastructure;

namespace Beutl.Editor.Operations;

public sealed class RemoveCollectionItemOperation : ChangeOperation, IPropertyPathProvider
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

    public override ChangeOperation CreateRevertOperation(OperationExecutionContext context, OperationSequenceGenerator sequenceGenerator)
    {
        var obj = context.FindObject(ObjectId)
            ?? throw new InvalidOperationException($"Object with ID {ObjectId} not found.");

        var name = PropertyPathHelper.GetPropertyNameFromPath(PropertyPath);
        var (item, index) = FindItemAndIndex(obj, name);

        var itemJson = CoreSerializer.SerializeToJsonNode(item);

        return new InsertCollectionItemOperation
        {
            SequenceNumber = sequenceGenerator.GetNext(),
            ObjectId = ObjectId,
            PropertyPath = PropertyPath,
            Item = itemJson,
            Index = index
        };
    }

    private (object item, int index) FindItemAndIndex(ICoreObject obj, string propertyName)
    {
        var coreProperty = PropertyRegistry.FindRegistered(obj.GetType(), propertyName);

        if (coreProperty != null)
        {
            if (obj.GetValue(coreProperty) is IList list)
            {
                var item = list.OfType<CoreObject>().FirstOrDefault(x => x.Id == ItemId)
                    ?? throw new InvalidOperationException($"Item with ID {ItemId} not found in property {PropertyPath}.");
                return (item, list.IndexOf(item));
            }
            throw new InvalidOperationException($"Property {propertyName} is not a list on type {obj.GetType().FullName}.");
        }

        if (obj is EngineObject engineObj)
        {
            var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == propertyName)
                ?? throw new InvalidOperationException($"Engine property {propertyName} not found on type {obj.GetType().FullName}.");

            if (engineProperty is IListProperty listProperty)
            {
                var item = listProperty.OfType<EngineObject>().FirstOrDefault(x => x.Id == ItemId)
                    ?? throw new InvalidOperationException($"Item with ID {ItemId} not found in property {PropertyPath}.");
                return (item, listProperty.IndexOf(item));
            }
            throw new InvalidOperationException($"Engine property {propertyName} is not a list on type {obj.GetType().FullName}.");
        }

        throw new InvalidOperationException($"Property {propertyName} not found on type {obj.GetType().FullName}.");
    }
}
