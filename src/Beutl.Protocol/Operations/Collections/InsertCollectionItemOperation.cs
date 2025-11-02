using System.Collections;
using System.Text.Json.Nodes;
using Beutl.Engine;

namespace Beutl.Protocol.Operations.Collections;

public sealed class InsertCollectionItemOperation : SyncOperation
{
    public required Guid ObjectId { get; set; }

    public required string PropertyName { get; set; }

    public required JsonObject Item { get; set; }

    public required int Index { get; set; }

    public override void Apply(OperationExecutionContext context)
    {
        var obj = context.FindObject(ObjectId)
            ?? throw new InvalidOperationException($"Object with ID {ObjectId} not found.");

        var coreProperty = PropertyRegistry.FindRegistered(obj.GetType(), PropertyName);

        var newItem = CoreSerializerHelper.DeserializeFromJsonObject(Item);

        if (coreProperty != null)
        {
            ApplyToCoreProperty(obj, coreProperty, newItem);
            return;
        }

        if (obj is EngineObject engineObj)
        {
            var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == PropertyName)
                ?? throw new InvalidOperationException($"Engine property {PropertyName} not found on type {obj.GetType().FullName}.");

            if (engineProperty is not IListProperty listProperty)
            {
                throw new InvalidOperationException($"Engine property {PropertyName} is not a list on type {obj.GetType().FullName}.");
            }

            ApplyToEngineProperty(listProperty, newItem);
        }
    }

    private void ApplyToEngineProperty(IListProperty listProperty, object newItem)
    {
        if (Index < 0 || Index > listProperty.Count)
        {
            listProperty.Add(newItem);
        }
        else
        {
            listProperty.Insert(Index, newItem);
        }
    }

    private void ApplyToCoreProperty(ICoreObject obj, CoreProperty coreProperty, object newItem)
    {
        if (obj.GetValue(coreProperty) is not IList list)
        {
            throw new InvalidOperationException($"Property {PropertyName} is not a list on type {obj.GetType().FullName}.");
        }

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
