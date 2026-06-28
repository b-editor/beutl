using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl.AgentToolkit.Reconciliation;

public sealed record ChangeSetEntry(
    string Operation,
    string Path,
    string? TargetId = null,
    JsonNode? OldValue = null,
    JsonNode? NewValue = null,
    int? Index = null);

public sealed record ReconcilePlan(
    IReadOnlyList<ChangeSetEntry> Changes,
    IReadOnlyList<ValidationOutcome> Validation)
{
    public JsonArray ExpectedChangeSet => ChangeSetJson.ToJsonArray(Changes);
}

public sealed record ReconcileResult(
    ReconcilePlan Plan,
    JsonObject Document);

public static class ChangeOperations
{
    public const string SetProperty = "set-property";
    public const string InsertChild = "insert-child";
    public const string RemoveChild = "remove-child";
    public const string MoveChild = "move-child";
}

internal static class ChangeSetJson
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static JsonArray ToJsonArray(IEnumerable<ChangeSetEntry> changes)
    {
        return new JsonArray(changes
            .Select(change => JsonSerializer.SerializeToNode(change, s_options))
            .ToArray());
    }
}
