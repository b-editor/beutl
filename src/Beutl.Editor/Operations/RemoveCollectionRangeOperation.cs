using System.Collections;
using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Serialization;

using Beutl.Editor.Infrastructure;

namespace Beutl.Editor.Operations;

public sealed class RemoveCollectionRangeOperation : ChangeOperation, IPropertyPathProvider
{
    public required Guid ObjectId { get; set; }

    public required string PropertyPath { get; set; }

    public required int Index { get; set; }

    public required int Count { get; set; }

    /// <summary>
    /// The JSON representation of the items that were removed.
    /// This is used by InsertCollectionRangeOperation.CreateRevertOperation.
    /// </summary>
    public JsonNode[]? Items { get; set; }

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
        listProperty.RemoveRange(Index, Count);
    }

    private void ApplyToCoreProperty(ICoreObject obj, CoreProperty coreProperty)
    {
        if (obj.GetValue(coreProperty) is not IList list)
        {
            throw new InvalidOperationException($"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        for (int i = 0; i < Count; i++)
        {
            list.RemoveAt(Index);
        }
    }

    public override ChangeOperation CreateRevertOperation(OperationExecutionContext context, OperationSequenceGenerator sequenceGenerator)
    {
        var obj = context.FindObject(ObjectId)
            ?? throw new InvalidOperationException($"Object with ID {ObjectId} not found.");

        var name = PropertyPathHelper.GetPropertyNameFromPath(PropertyPath);
        var list = GetList(obj, name);

        // Serialize items at the positions to be removed
        var items = new JsonNode[Count];
        for (int i = 0; i < Count; i++)
        {
            items[i] = CoreSerializer.SerializeToJsonNode(list[Index + i]!);
        }

        return new InsertCollectionRangeOperation
        {
            SequenceNumber = sequenceGenerator.GetNext(),
            ObjectId = ObjectId,
            PropertyPath = PropertyPath,
            Items = items,
            Index = Index
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
