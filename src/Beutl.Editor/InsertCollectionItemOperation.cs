using System.Collections;
using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.Editor;

public sealed class InsertCollectionItemOperation : ChangeOperation, IPropertyPathProvider
{
    public required Guid ObjectId { get; set; }

    public required string PropertyPath { get; set; }

    public required JsonNode Item { get; set; }

    public required int Index { get; set; }

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
                                 ?? throw new InvalidOperationException(
                                     $"Engine property {PropertyPath} not found on type {obj.GetType().FullName}.");

            if (engineProperty is not IListProperty listProperty)
            {
                throw new InvalidOperationException(
                    $"Engine property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
            }

            ApplyToEngineProperty(listProperty);
        }
    }

    private void ApplyToEngineProperty(IListProperty listProperty)
    {
        var elementType = listProperty.ElementType;
        var newItem = CoreSerializer.DeserializeFromJsonNode(Item, elementType,
            new CoreSerializerOptions { BaseUri = listProperty.GetOwnerObject()?.FindBaseUri() });
        if (Index < 0 || Index > listProperty.Count)
        {
            listProperty.Add(newItem);
        }
        else
        {
            listProperty.Insert(Index, newItem);
        }
    }

    private void ApplyToCoreProperty(ICoreObject obj, CoreProperty coreProperty)
    {
        if (obj.GetValue(coreProperty) is not IList list)
        {
            throw new InvalidOperationException(
                $"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        var elementType = ArrayTypeHelpers.GetElementType(list.GetType());
        if (elementType is null) throw new InvalidOperationException("Could not determine element type of the list.");

        var newItem = CoreSerializer.DeserializeFromJsonNode(Item, elementType, new CoreSerializerOptions
        {
            BaseUri = obj?.FindBaseUri(),
        });

        if (Index < 0 || Index > list.Count)
        {
            list.Add(newItem);
        }
        else
        {
            list.Insert(Index, newItem);
        }
    }

    public override ChangeOperation CreateRevertOperation(OperationExecutionContext context, OperationSequenceGenerator sequenceGenerator)
    {
        var obj = context.FindObject(ObjectId)
                  ?? throw new InvalidOperationException($"Object with ID {ObjectId} not found.");

        var name = PropertyPathHelper.GetPropertyNameFromPath(PropertyPath);
        var list = GetList(obj, name);

        // Get the item at the index position and serialize it
        var item = list[Index];
        var itemJson = CoreSerializer.SerializeToJsonNode(item!);

        return new RemoveCollectionRangeOperation
        {
            SequenceNumber = sequenceGenerator.GetNext(),
            ObjectId = ObjectId,
            PropertyPath = PropertyPath,
            Index = Index,
            Count = 1,
            Items = [itemJson]
        };
    }

    private static IList GetList(ICoreObject obj, string propertyName)
    {
        var coreProperty = PropertyRegistry.FindRegistered(obj.GetType(), propertyName);

        if (coreProperty != null)
        {
            if (obj.GetValue(coreProperty) is IList list)
            {
                return list;
            }
            throw new InvalidOperationException($"Property {propertyName} is not a list on type {obj.GetType().FullName}.");
        }

        if (obj is EngineObject engineObj)
        {
            var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == propertyName)
                                 ?? throw new InvalidOperationException($"Engine property {propertyName} not found on type {obj.GetType().FullName}.");

            if (engineProperty is IListProperty listProperty)
            {
                return listProperty;
            }
            throw new InvalidOperationException($"Engine property {propertyName} is not a list on type {obj.GetType().FullName}.");
        }

        throw new InvalidOperationException($"Property {propertyName} not found on type {obj.GetType().FullName}.");
    }
}
