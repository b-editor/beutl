using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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
    [McpServerTool(Name = "plan_composition")]
    [Description("Dry-runs an explicitly named reusable composition template without returning a huge patch. Use only when the user asked for a template/starter or a named composition style.")]
    public ToolResult<PlanCompositionResponse> PlanComposition(
        string? name = null,
        string? tag = null,
        JsonObject? inputProps = null,
        string? seed = null,
        bool includeDetailedPlan = false)
    {
        return Execute(() =>
        {
            RequireCompositionName(name);
            IEditingSession session = sessions.RequireSession();
            CompositionRender composition = _compositionCatalog.Render(
                name,
                tag,
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
    [Description("Applies an explicitly named reusable composition template through the declarative editor loop. Prefer planId from plan_composition for compact plan/apply parity; expectedChangeSet remains supported.")]
    public ToolResult<ApplyCompositionResponse> ApplyComposition(
        string? name = null,
        string? tag = null,
        JsonObject? inputProps = null,
        string? seed = null,
        string? planId = null,
        JsonNode? expectedChangeSet = null)
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

            RequireCompositionName(name);
            CompositionRender composition = _compositionCatalog.Render(
                name,
                tag,
                inputProps,
                sessions.ResolveCompositionSeed(seed));
            JsonArray? normalizedExpectedChangeSet = NormalizeExpectedChangeSet(expectedChangeSet);
            ReconcileResult result = _reconciler.ApplyValidated(
                session,
                current =>
                {
                    JsonObject patch = CompositionTemplateCatalog.OffsetPatchElementStarts(
                        composition.Patch, ReadSceneStart(current));
                    ResolvedEdit resolved = ResolvePatchEdit(current, patch, SchemaVersion.Current);
                    return (resolved.Document, resolved.KnownNewIds);
                },
                plan => normalizedExpectedChangeSet is null || ChangeSetMatches(plan, normalizedExpectedChangeSet)
                    ? null
                    : new ToolError(
                        ErrorCode.ValidationRejected,
                        "The live composition change set differs from expectedChangeSet.",
                        null,
                        "Run plan_composition again and submit the updated expectedChangeSet."));
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
            "Pass planId to apply_composition for compact plan/apply parity. Set includeDetailedPlan=true only if you explicitly need expectedChangeSet.");
    }

    private static TimeSpan ReadSceneStart(JsonObject current)
    {
        return current["Start"] is JsonValue value
               && value.TryGetValue(out string? text)
               && TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out TimeSpan start)
            ? start
            : TimeSpan.Zero;
    }

    private static void RequireCompositionName(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            "Composition templates require an explicit template name.",
            null,
            "Call list_compositions to inspect options, then pass a returned name only when the user explicitly asked for a reusable template/starter. For original creative briefs, author a custom patch and pass it to apply_edit."));
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

    private static JsonArray? NormalizeExpectedChangeSet(JsonNode? expectedChangeSet)
    {
        if (expectedChangeSet is null)
        {
            return null;
        }

        if (expectedChangeSet is JsonArray array)
        {
            if (array.Count == 1 && TryReadString(array[0], out string? singleText))
            {
                return ParseExpectedChangeSet(singleText);
            }

            var normalized = new JsonArray();
            foreach (JsonNode? item in array)
            {
                if (item is JsonObject obj)
                {
                    normalized.Add(obj.DeepClone());
                }
                else if (TryReadString(item, out string? text))
                {
                    JsonNode? parsed = JsonNode.Parse(text);
                    if (parsed is not JsonObject parsedObject)
                    {
                        throw InvalidExpectedChangeSet();
                    }

                    normalized.Add(parsedObject);
                }
                else
                {
                    throw InvalidExpectedChangeSet();
                }
            }

            return normalized;
        }

        if (TryReadString(expectedChangeSet, out string? serialized))
        {
            return ParseExpectedChangeSet(serialized);
        }

        throw InvalidExpectedChangeSet();
    }

    private static JsonArray ParseExpectedChangeSet(string serialized)
    {
        try
        {
            return JsonNode.Parse(serialized) switch
            {
                JsonArray parsedArray => parsedArray,
                JsonObject parsedObject => new JsonArray(parsedObject),
                _ => throw InvalidExpectedChangeSet()
            };
        }
        catch (JsonException)
        {
            throw InvalidExpectedChangeSet();
        }
    }

    private static bool TryReadString(JsonNode? node, [NotNullWhen(true)] out string? text)
    {
        text = null;
        if (node is null || node.GetValueKind() != JsonValueKind.String)
        {
            return false;
        }

        text = node.GetValue<string>();
        return true;
    }

    private static ReconcileException InvalidExpectedChangeSet()
    {
        return new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            "expectedChangeSet must be the JSON array returned by plan_composition, not a shorthand summary.",
            null,
            "Pass plan_composition.expectedChangeSet verbatim. If a client exposes it as strings, pass either the whole JSON array string or one JSON object string per change entry; do not replace it with text like '2 changes'."));
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
