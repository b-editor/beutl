using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Beutl.AgentToolkit.Reconciliation;

public sealed record ChangeSetEntry(
    string Operation,
    string Path,
    string? TargetId = null,
    JsonNode? OldValue = null,
    JsonNode? NewValue = null,
    int? Index = null);

public sealed record ReconcilePlan
{
    public ReconcilePlan(
        IReadOnlyList<ChangeSetEntry> changes,
        IReadOnlyList<ValidationOutcome> validation)
    {
        Changes = changes;
        Validation = validation;
    }

    [JsonPropertyName("changes")]
    public IReadOnlyList<ChangeSetEntry> Changes { get; init; }

    [JsonPropertyName("validation")]
    public IReadOnlyList<ValidationOutcome> Validation { get; init; }

    public int ChangeCount => Changes.Count;

    public IReadOnlyDictionary<string, int> Operations => Changes
        .GroupBy(change => change.Operation, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    public int ValidationCount => Validation.Count;

    public IReadOnlyDictionary<string, int> ValidationStatuses => Validation
        .GroupBy(outcome => outcome.Status.ToString(), StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    public bool Valid => Validation.All(item => item.Status != ValidationStatus.Rejected);

    // The change set a stored plan is checked against when apply_composition gets its planId.
    [JsonIgnore]
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
