using System.Collections;
using Beutl.Engine;

namespace Beutl.Protocol.Operations.Collections;

public sealed class RemoveCollectionRangeOperation : SyncOperation
{
    public required Guid ObjectId { get; set; }

    public required string PropertyName { get; set; }

    public required int Index { get; set; }

    public required int Count { get; set; }

    public override void Apply(OperationExecutionContext context)
    {
        var obj = context.FindObject(ObjectId)
            ?? throw new InvalidOperationException($"Object with ID {ObjectId} not found.");

        var coreProperty = PropertyRegistry.FindRegistered(obj.GetType(), PropertyName);

        if (coreProperty != null)
        {
            ApplyToCoreProperty(obj, coreProperty);
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
            throw new InvalidOperationException($"Property {PropertyName} is not a list on type {obj.GetType().FullName}.");
        }

        for (int i = 0; i < Count; i++)
        {
            list.RemoveAt(Index);
        }
    }
}
