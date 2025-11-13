namespace Beutl.Protocol.Queries;

public class MutationExecutor
{
    private readonly QueryExecutor _queryExecutor;

    public MutationExecutor()
    {
        _queryExecutor = new QueryExecutor();
    }

    public MutationResult Execute(ICoreObject root, Mutation mutation, QuerySchema? returnSchema = null)
    {
        var context = new MutationContext(root);
        MutationResult result = mutation.Execute(context);

        if (result.Success && returnSchema != null)
        {
            // If mutation succeeded and a return schema is provided, query the updated data
            if (mutation is UpdatePropertyMutation updateMutation)
            {
                QueryResult? queryResult = _queryExecutor.ExecuteById(root, updateMutation.TargetId, returnSchema);
                result.Metadata["queryResult"] = queryResult?.Data;
            }
            else if (mutation is CreateObjectMutation createMutation && result.Data is Dictionary<string, object?> data && data.TryGetValue("objectId", out object? objId) && objId is Guid id)
            {
                QueryResult? queryResult = _queryExecutor.ExecuteById(root, id, returnSchema);
                result.Metadata["queryResult"] = queryResult?.Data;
            }
        }

        return result;
    }
}
