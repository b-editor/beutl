using System.Collections;

namespace Beutl.Protocol.Queries;

public class AddToCollectionMutation : Mutation
{
    public AddToCollectionMutation(Guid targetId, string collectionPropertyName, object item, int? index = null)
    {
        TargetId = targetId;
        CollectionPropertyName = collectionPropertyName;
        Item = item;
        Index = index;
    }

    public Guid TargetId { get; }
    public string CollectionPropertyName { get; }
    public object Item { get; }
    public int? Index { get; }

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

        try
        {
            // Get the element type of the collection
            Type elementType = property.PropertyType.IsGenericType
                ? property.PropertyType.GetGenericArguments()[0]
                : typeof(object);

            object? convertedItem = UpdatePropertyMutation.ConvertValue(Item, elementType);

            if (Index.HasValue && Index.Value >= 0 && Index.Value <= list.Count)
            {
                list.Insert(Index.Value, convertedItem);
            }
            else
            {
                list.Add(convertedItem);
            }

            return MutationResult.CreateSuccess(
                new { targetId = TargetId, collectionPropertyName = CollectionPropertyName, addedAt = Index ?? list.Count - 1 },
                new Dictionary<string, object?>
                {
                    ["mutationType"] = "AddToCollection",
                    ["executedAt"] = DateTime.UtcNow
                });
        }
        catch (Exception ex)
        {
            return MutationResult.CreateError($"Failed to add item to collection: {ex.Message}");
        }
    }
}
