using System.Collections;

namespace Beutl.Protocol.Queries;

public class DeleteObjectMutation : Mutation
{
    public DeleteObjectMutation(Guid targetId)
    {
        TargetId = targetId;
    }

    public Guid TargetId { get; }

    public override MutationResult Execute(MutationContext context)
    {
        ICoreObject? target = context.Root.FindById(TargetId);
        if (target == null)
        {
            return MutationResult.CreateError($"Object with ID {TargetId} not found.");
        }

        // Find parent
        if (target is not IHierarchical hierarchical || hierarchical.HierarchicalParent == null)
        {
            return MutationResult.CreateError($"Cannot delete root object or object without parent.");
        }

        if (hierarchical.HierarchicalParent is not ICoreObject parent)
        {
            return MutationResult.CreateError($"Parent is not an ICoreObject.");
        }

        try
        {
            // Find which property contains this object
            foreach (CoreProperty property in PropertyRegistry.GetRegistered(parent.GetType()))
            {
                object? value = parent.GetValue(property);

                if (value == target)
                {
                    parent.SetValue(property, null);
                    return MutationResult.CreateSuccess(
                        new { deletedId = TargetId },
                        new Dictionary<string, object?>
                        {
                            ["mutationType"] = "DeleteObject",
                            ["executedAt"] = DateTime.UtcNow
                        });
                }

                if (value is IList list && list.Contains(target))
                {
                    list.Remove(target);
                    return MutationResult.CreateSuccess(
                        new { deletedId = TargetId },
                        new Dictionary<string, object?>
                        {
                            ["mutationType"] = "DeleteObject",
                            ["executedAt"] = DateTime.UtcNow
                        });
                }
            }

            return MutationResult.CreateError($"Could not find object in parent's properties.");
        }
        catch (Exception ex)
        {
            return MutationResult.CreateError($"Failed to delete object: {ex.Message}");
        }
    }
}
