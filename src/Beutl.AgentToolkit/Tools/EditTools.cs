using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Serialization;
using ModelContextProtocol.Server;
using MergePatchApplier = Beutl.AgentToolkit.MergePatch.MergePatch;

namespace Beutl.AgentToolkit.Tools;

[McpServerToolType]
public sealed class EditTools(AgentSessionManager sessions) : ToolBase
{
    private readonly Reconciler _reconciler = new();

    [McpServerTool(Name = "plan_edit")]
    [Description("Computes the change set and validation outcomes for a desired declarative document without mutating the current session.")]
    public ToolResult<ReconcilePlan> PlanEdit(JsonObject? desired = null, JsonObject? patch = null, string? schemaVersion = null)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            return _reconciler.Plan(session, ResolveDesiredDocument(session, desired, patch, schemaVersion));
        });
    }

    [McpServerTool(Name = "apply_edit")]
    [Description("Applies a full desired declarative document atomically to the current session through Beutl history.")]
    public ToolResult<ReconcileResult> ApplyEdit(
        JsonObject? desired = null,
        JsonObject? patch = null,
        string? schemaVersion = null,
        JsonArray? expectedChangeSet = null)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            JsonObject resolved = ResolveDesiredDocument(session, desired, patch, schemaVersion);
            ReconcilePlan plan = _reconciler.Plan(session, resolved);
            if (expectedChangeSet is not null && !ChangeSetMatches(plan, expectedChangeSet))
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "The live change set differs from expectedChangeSet.",
                    null,
                    "Run plan_edit again and submit the updated expectedChangeSet."));
            }

            return _reconciler.Apply(session, resolved);
        });
    }

    private static JsonObject ResolveDesiredDocument(
        IEditingSession session,
        JsonObject? desired,
        JsonObject? patch,
        string? schemaVersion)
    {
        if ((desired is null) == (patch is null))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "Supply exactly one of desired or patch."));
        }

        if (desired is not null)
        {
            JsonObject document = (JsonObject)desired.DeepClone();
            if (schemaVersion is not null)
            {
                document[SchemaVersion.PropertyName] = schemaVersion;
            }

            return document;
        }

        SchemaVersion.EnsureKnown(schemaVersion);
        JsonObject current = session.Documents.Read(session.Root);
        JsonNode? merged = MergePatchApplier.Apply(current, patch);
        if (merged is not JsonObject mergedObject)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "Patch must produce a document object."));
        }

        SchemaVersion.Stamp(mergedObject);
        return mergedObject;
    }

    private static bool ChangeSetMatches(ReconcilePlan plan, JsonArray expectedChangeSet)
    {
        if (plan.Changes.Count != expectedChangeSet.Count)
        {
            return false;
        }

        for (int i = 0; i < plan.Changes.Count; i++)
        {
            if (expectedChangeSet[i] is not JsonObject expected
                || !EntryMatches(plan.Changes[i], expected))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EntryMatches(ChangeSetEntry actual, JsonObject expected)
    {
        return string.Equals(actual.Operation, ReadString(expected, nameof(ChangeSetEntry.Operation)), StringComparison.Ordinal)
               && string.Equals(actual.Path, ReadString(expected, nameof(ChangeSetEntry.Path)), StringComparison.Ordinal)
               && string.Equals(actual.TargetId, ReadNullableString(expected, nameof(ChangeSetEntry.TargetId)), StringComparison.Ordinal)
               && actual.Index == ReadNullableInt(expected, nameof(ChangeSetEntry.Index))
               && JsonNode.DeepEquals(actual.OldValue, ReadNode(expected, nameof(ChangeSetEntry.OldValue)))
               && JsonNode.DeepEquals(actual.NewValue, ReadNode(expected, nameof(ChangeSetEntry.NewValue)));
    }

    private static JsonNode? ReadNode(JsonObject obj, string name)
    {
        return obj[name] ?? obj[JsonNamingPolicy.CamelCase.ConvertName(name)];
    }

    private static string? ReadString(JsonObject obj, string name)
    {
        return ReadNode(obj, name)?.GetValue<string>();
    }

    private static string? ReadNullableString(JsonObject obj, string name)
    {
        JsonNode? node = ReadNode(obj, name);
        return node?.GetValueKind() == JsonValueKind.Null ? null : node?.GetValue<string>();
    }

    private static int? ReadNullableInt(JsonObject obj, string name)
    {
        JsonNode? node = ReadNode(obj, name);
        return node?.GetValueKind() == JsonValueKind.Null ? null : node?.GetValue<int>();
    }
}
