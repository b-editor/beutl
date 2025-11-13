using System.Collections;
using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.Protocol.Operations.Collections;

public sealed class InsertCollectionItemOperation : SyncOperation, IPropertyPathProvider
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
        var elementType = listProperty.ElementType;
        var newItem = CoreSerializerHelper.DeserializeFromJsonNode(Item, elementType);
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
            throw new InvalidOperationException($"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        var elementType = ArrayTypeHelpers.GetElementType(list.GetType());
        if (elementType is null) throw new InvalidOperationException("Could not determine element type of the list.");

        var newItem = CoreSerializerHelper.DeserializeFromJsonNode(Item, elementType);

        if (Index < 0 || Index > list.Count)
        {
            list.Add(newItem);
        }
        else
        {
            list.Insert(Index, newItem);
        }
    }
}
