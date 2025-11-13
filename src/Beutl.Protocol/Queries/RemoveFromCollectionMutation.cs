using System.Collections;

namespace Beutl.Protocol.Queries;

public class RemoveFromCollectionMutation : Mutation
{
    public RemoveFromCollectionMutation(Guid targetId, string collectionPropertyName, int index)
    {
        TargetId = targetId;
        CollectionPropertyName = collectionPropertyName;
        Index = index;
    }

    public Guid TargetId { get; }
    public string CollectionPropertyName { get; }
    public int Index { get; }

    public override MutationResult Execute(MutationContext context)
    {
        ICoreObject? target = context.Root.FindById(TargetId);
        if (target == null)
        {
            return MutationResult.CreateError($"Object with ID {TargetId} not found.");
        }

        CoreProperty? property = PropertyRegistry.FindRegistered(target, CollectionPropertyName);
        if (property == null)
        {
            return MutationResult.CreateError($"Property '{CollectionPropertyName}' not found.");
        }

        object? collection = target.GetValue(property);
        if (collection is not IList list)
        {
            return MutationResult.CreateError($"Property '{CollectionPropertyName}' is not a collection.");
        }

        if (Index < 0 || Index >= list.Count)
        {
            return MutationResult.CreateError($"Index {Index} is out of range for collection '{CollectionPropertyName}'.");
        }

        try
        {
            object? removedItem = list[Index];
            list.RemoveAt(Index);

            return MutationResult.CreateSuccess(
                new { targetId = TargetId, collectionPropertyName = CollectionPropertyName, removedIndex = Index },
                new Dictionary<string, object?>
                {
                    ["mutationType"] = "RemoveFromCollection",
                    ["executedAt"] = DateTime.UtcNow
                });
        }
        catch (Exception ex)
        {
            return MutationResult.CreateError($"Failed to remove item from collection: {ex.Message}");
        }
    }
}
