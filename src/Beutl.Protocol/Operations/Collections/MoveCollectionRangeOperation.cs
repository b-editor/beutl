using System.Collections;
using Beutl.Engine;

namespace Beutl.Protocol.Operations.Collections;

public sealed class MoveCollectionRangeOperation : SyncOperation
{
    public required Guid ObjectId { get; set; }

    public required string PropertyPath { get; set; }

    public required int OldIndex { get; set; }

    public required int NewIndex { get; set; }

    public required int Count { get; set; }

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
        listProperty.MoveRange(OldIndex, NewIndex, Count);
    }

    private void ApplyToCoreProperty(ICoreObject obj, CoreProperty coreProperty)
    {
        if (obj.GetValue(coreProperty) is not IList list)
        {
            throw new InvalidOperationException($"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        var items = new object[Count];
        for (int i = 0; i < Count; i++)
        {
            items[i] = list[OldIndex]!;
            list.RemoveAt(OldIndex);
        }

        for (int i = 0; i < Count; i++)
        {
            list.Insert(NewIndex + i, items[i]);
        }
    }
}
