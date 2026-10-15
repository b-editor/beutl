using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public sealed partial class EditTools
{
    private const string CustomPatchAdvice =
        "For original creative briefs, author a custom patch and pass it to apply_edit.";

    [McpServerTool(Name = "plan_composition")]
    [Description("Dry-runs an explicitly named reusable composition template without returning a huge patch. Use only when the user asked for a template/starter or a named composition style.")]
    public ToolResult<PlanCompositionResponse> PlanComposition(
        string? name = null,
        // Unused because a name is required; kept so calls that pass it stay valid.
        string? tag = null,
        JsonObject? inputProps = null,
        string? seed = null,
        bool includeDetailedPlan = false)
    {
        return Execute(() =>
        {
            CompositionTemplateCatalog.RequireCompositionName(name, CustomPatchAdvice);
            IEditingSession session = sessions.RequireSession();
            CompositionRender composition = _compositionCatalog.Render(
                name,
                inputProps,
                sessions.ResolveCompositionSeed(seed));
            // Resolve the patch and plan inside the session dispatch so a LiveEditor plan does not
            // read the UI-owned scene on the MCP request thread.
            ResolvedEdit resolved = null!;
            ReconcilePlan plan = _reconciler.PlanFromCurrent(session, current =>
            {
                JsonObject patch = CompositionTemplateCatalog.OffsetPatchElementStarts(
                    composition.Patch, ReadSceneStart(current));
                resolved = ResolvePatchEdit(current, patch, SchemaVersion.Current);
                return (resolved.Document, resolved.KnownNewIds);
            });
            CompositionPlanState state = sessions.StoreCompositionPlan(
                sessions.GetSessionKey(session),
                composition.Name,
                composition.Seed,
                composition.InputProps,
                resolved.Document,
                plan.ExpectedChangeSet,
                resolved.KnownNewIds);

            return new PlanCompositionResponse(
                SchemaVersion.Current,
                state.Id,
                CreateCompositionRunSummary(composition),
                CreateCompositionPlanPreview(plan),
                includeDetailedPlan ? plan : null);
        });
    }

    [McpServerTool(Name = "apply_composition")]
    [Description("Applies an explicitly named reusable composition template through the declarative editor loop. With planId from plan_composition, applies that plan and refuses it when the scene changed since.")]
    public ToolResult<ApplyCompositionResponse> ApplyComposition(
        string? name = null,
        // Unused because a name is required; kept so calls that pass it stay valid.
        string? tag = null,
        JsonObject? inputProps = null,
        string? seed = null,
        string? planId = null)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            if (!string.IsNullOrWhiteSpace(planId))
            {
                // Validate the plan against the captured session's key, not the current one:
                // ApplyValidated mutates this captured session, so a swap between RequireSession and
                // the lookup must not let a plan for the swapped-in project apply to this scene.
                CompositionPlanState state = sessions.GetCompositionPlan(
                    planId.Trim(), sessions.GetSessionKey(session));
                ReconcileResult storedResult = _reconciler.ApplyValidated(
                    session,
                    _ => ((JsonObject)state.DesiredDocument.DeepClone(), state.KnownNewIds.ToHashSet()),
                    plan => ChangeSetMatches(plan, state.ExpectedChangeSet)
                        ? null
                        : new ToolError(
                            ErrorCode.ValidationRejected,
                            "The live composition change set differs from the stored planId.",
                            planId,
                            "Run plan_composition again and pass the new planId."));
                sessions.RemoveCompositionPlan(state.Id);
                return new ApplyCompositionResponse(
                    SchemaVersion.Current,
                    CreateCompositionRunSummary(_compositionCatalog.Render(
                        state.CompositionName,
                        inputProps: state.InputProps,
                        seed: state.Seed)),
                    state.Id,
                    storedResult);
            }

            CompositionTemplateCatalog.RequireCompositionName(name, CustomPatchAdvice);
            CompositionRender composition = _compositionCatalog.Render(
                name,
                inputProps,
                sessions.ResolveCompositionSeed(seed));
            ReconcileResult result = _reconciler.ApplyFromCurrent(
                session,
                current =>
                {
                    JsonObject patch = CompositionTemplateCatalog.OffsetPatchElementStarts(
                        composition.Patch, ReadSceneStart(current));
                    ResolvedEdit resolved = ResolvePatchEdit(current, patch, SchemaVersion.Current);
                    return (resolved.Document, resolved.KnownNewIds);
                });
            return new ApplyCompositionResponse(
                SchemaVersion.Current,
                CreateCompositionRunSummary(composition),
                null,
                result);
        });
    }

    private static CompositionRunSummary CreateCompositionRunSummary(CompositionRender composition)
    {
        return new CompositionRunSummary(
            composition.Name,
            composition.Seed,
            (JsonObject)composition.InputProps.DeepClone(),
            (JsonObject)composition.ResolvedProps.DeepClone(),
            composition.Metadata,
            composition.Sequences.ToArray(),
            composition.Transitions.ToArray());
    }

    private static CompositionPlanPreview CreateCompositionPlanPreview(ReconcilePlan plan)
    {
        return new CompositionPlanPreview(
            plan.Valid,
            plan.Changes.Count,
            plan.Changes
                .GroupBy(change => change.Operation, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            "Set includeDetailedPlan=true to list every change.");
    }

    private static TimeSpan ReadSceneStart(JsonObject current)
    {
        return current["Start"] is JsonValue value
               && value.TryGetValue(out string? text)
               && TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out TimeSpan start)
            ? start
            : TimeSpan.Zero;
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

    // A stored plan's change set comes from ChangeSetJson, which writes camelCase names.
    private static JsonNode? ReadNode(JsonObject obj, string name)
    {
        return obj[JsonNamingPolicy.CamelCase.ConvertName(name)];
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
