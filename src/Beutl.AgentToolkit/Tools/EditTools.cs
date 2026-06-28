using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.Serialization;
using ModelContextProtocol.Server;
using MergePatchApplier = Beutl.AgentToolkit.MergePatch.MergePatch;

namespace Beutl.AgentToolkit.Tools;

public sealed record CompositionRunSummary(
    string Name,
    string Seed,
    JsonObject InputProps,
    JsonObject ResolvedProps,
    CompositionMetadata Metadata,
    IReadOnlyList<CompositionSequenceDescriptor> Sequences,
    IReadOnlyList<CompositionTransitionDescriptor> Transitions);

public sealed record CompositionPlanPreview(
    bool Valid,
    int ChangeCount,
    IReadOnlyDictionary<string, int> Operations,
    string UsageHint);

public sealed record PlanCompositionResponse(
    string SchemaVersion,
    string PlanId,
    CompositionRunSummary Composition,
    CompositionPlanPreview Plan,
    ReconcilePlan? DetailedPlan);

public sealed record ApplyCompositionResponse(
    string SchemaVersion,
    CompositionRunSummary Composition,
    string? AppliedPlanId,
    ReconcileResult Result);

public sealed record AppliedEntityId(
    string Id,
    string Path,
    string? Type,
    string? Name);

public sealed record ApplyEditResponse(
    bool Valid,
    IReadOnlyList<ChangeSetEntry> Changes,
    IReadOnlyList<ValidationOutcome> Validation,
    JsonArray AppliedChangeSet,
    IReadOnlyList<AppliedEntityId> CreatedIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JsonObject? Document = null);

internal sealed record ResolvedEdit(JsonObject Document, HashSet<Guid> KnownNewIds);

[McpServerToolType]
public sealed class EditTools(AgentSessionManager sessions) : ToolBase
{
    private readonly Reconciler _reconciler = new();
    private readonly CompositionTemplateCatalog _compositionCatalog = new();

    [McpServerTool(Name = "plan_edit")]
    [Description("Dry-runs a declarative edit without mutating the session. In the in-app host, call attach_active_editor first. Supply exactly one of desired or patch; prefer patch for targeted edits.")]
    public ToolResult<ReconcilePlan> PlanEdit(
        [Description("Full desired declarative document. Uses PascalCase properties, $type discriminators, stable Id fields, typed properties for transforms/geometry/pens/brushes/effects, Animations.<Property>.KeyFrames for keyframes, and schemaVersion. Full desired documents are authoritative: omitted child arrays such as Elements or Objects can delete existing content, so use patch for partial edits.")]
        JsonObject? desired = null,
        [Description("JSON Merge Patch. Objects follow RFC 7396; Id-bearing arrays such as Elements, Objects, GradientStops, transform/effect Children, audio effect Children, and KeyFrames are merged by Id. Omit Id to insert; use {Id,$delete:true} to delete; use $index/$after/$before to reorder non-keyframe arrays. Unmentioned siblings are preserved.")]
        JsonObject? patch = null,
        [Description("Declarative document schema version. Required for patch; for desired, pass it here or include schemaVersion in the document. Mismatches are rejected instead of silently dropping content.")]
        string? schemaVersion = null)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            ResolvedEdit resolved = ResolveDesiredDocument(session, desired, patch, schemaVersion);
            return _reconciler.Plan(session, resolved.Document, resolved.KnownNewIds);
        });
    }

    [McpServerTool(Name = "apply_edit")]
    [Description("Atomically applies a declarative desired document or JSON Merge Patch through Beutl history. In the in-app host, call attach_active_editor first. Pass plan_edit.expectedChangeSet to guarantee plan/apply parity. The response is compact by default: appliedChangeSet plus createdIds for follow-up edits. Set includeDocument=true only when you need the full updated document.")]
    public ToolResult<ApplyEditResponse> ApplyEdit(
        [Description("Full desired declarative document. Uses PascalCase properties, $type discriminators, stable Id fields, typed properties for transforms/geometry/pens/brushes/effects, Animations.<Property>.KeyFrames for keyframes, and schemaVersion. Full desired documents are authoritative: omitted child arrays such as Elements or Objects can delete existing content, so use patch for partial edits.")]
        JsonObject? desired = null,
        [Description("JSON Merge Patch. Objects follow RFC 7396; Id-bearing arrays such as Elements, Objects, GradientStops, transform/effect Children, audio effect Children, and KeyFrames are merged by Id. Omit Id to insert; use {Id,$delete:true} to delete; use $index/$after/$before to reorder non-keyframe arrays. Unmentioned siblings are preserved.")]
        JsonObject? patch = null,
        [Description("Declarative document schema version. Required for patch; for desired, pass it here or include schemaVersion in the document. Mismatches are rejected instead of silently dropping content.")]
        string? schemaVersion = null,
        [Description("Optional change set returned by plan_edit.expectedChangeSet. apply_edit rejects the edit if the live computed change set differs.")]
        JsonNode? expectedChangeSet = null,
        [Description("Return the full updated document. Defaults to false to keep apply_edit responses compact; prefer createdIds or read_document_summary for follow-up edits.")]
        bool includeDocument = false)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            ResolvedEdit resolved = ResolveDesiredDocument(session, desired, patch, schemaVersion);
            ReconcilePlan plan = _reconciler.Plan(session, resolved.Document, resolved.KnownNewIds);
            JsonArray? normalizedExpectedChangeSet = NormalizeExpectedChangeSet(expectedChangeSet);
            if (normalizedExpectedChangeSet is not null && !ChangeSetMatches(plan, normalizedExpectedChangeSet))
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "The live change set differs from expectedChangeSet.",
                    null,
                    "Run plan_edit again and submit the updated expectedChangeSet."));
            }

            ReconcileResult result = _reconciler.Apply(session, resolved.Document, resolved.KnownNewIds);
            if (CompositionTemplateCatalog.TryInferTemplateName(patch ?? desired!) is { } inferredName)
            {
                sessions.RecordCompositionUse(inferredName);
            }

            return CreateApplyEditResponse(result, includeDocument);
        });
    }

    [McpServerTool(Name = "plan_composition")]
    [Description("Dry-runs an explicitly named reusable composition template without returning a huge patch. Use only when the user asked for a template/starter or a named composition style.")]
    public ToolResult<PlanCompositionResponse> PlanComposition(
        string? name = null,
        string? tag = null,
        JsonObject? inputProps = null,
        string? seed = null,
        bool avoidRecent = true,
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
                sessions.ResolveCompositionSeed(seed),
                avoidRecent ? sessions.GetAvoidedCompositions() : null,
                EnforceFirstSelection(name, avoidRecent));
            ResolvedEdit resolved = ResolveDesiredDocument(session, desired: null, patch: composition.Patch, schemaVersion: SchemaVersion.Current);
            ReconcilePlan plan = _reconciler.Plan(session, resolved.Document, resolved.KnownNewIds);
            CompositionPlanState state = sessions.StoreCompositionPlan(
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
        bool avoidRecent = true,
        JsonNode? expectedChangeSet = null)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            if (!string.IsNullOrWhiteSpace(planId))
            {
                CompositionPlanState state = sessions.GetCompositionPlan(planId.Trim());
                ReconcilePlan storedPlan = _reconciler.Plan(
                    session,
                    (JsonObject)state.DesiredDocument.DeepClone(),
                    state.KnownNewIds.ToHashSet());
                if (!ChangeSetMatches(storedPlan, state.ExpectedChangeSet))
                {
                    throw new ReconcileException(new ToolError(
                        ErrorCode.ValidationRejected,
                        "The live composition change set differs from the stored planId.",
                        planId,
                        "Run plan_composition again and pass the new planId."));
                }

                ReconcileResult storedResult = _reconciler.Apply(
                    session,
                    (JsonObject)state.DesiredDocument.DeepClone(),
                    state.KnownNewIds.ToHashSet());
                sessions.RecordCompositionUse(state.CompositionName);
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
                sessions.ResolveCompositionSeed(seed),
                avoidRecent ? sessions.GetAvoidedCompositions() : null,
                EnforceFirstSelection(name, avoidRecent));
            ResolvedEdit resolved = ResolveDesiredDocument(session, desired: null, patch: composition.Patch, schemaVersion: SchemaVersion.Current);
            ReconcilePlan plan = _reconciler.Plan(session, resolved.Document, resolved.KnownNewIds);
            JsonArray? normalizedExpectedChangeSet = NormalizeExpectedChangeSet(expectedChangeSet);
            if (normalizedExpectedChangeSet is not null && !ChangeSetMatches(plan, normalizedExpectedChangeSet))
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "The live composition change set differs from expectedChangeSet.",
                    null,
                    "Run plan_composition again and submit the updated expectedChangeSet."));
            }

            ReconcileResult result = _reconciler.Apply(session, resolved.Document, resolved.KnownNewIds);
            sessions.RecordCompositionUse(composition.Name);
            return new ApplyCompositionResponse(
                SchemaVersion.Current,
                CreateCompositionRunSummary(composition),
                null,
                result);
        });
    }

    private static ResolvedEdit ResolveDesiredDocument(
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

            return new ResolvedEdit(document, []);
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
        return new ResolvedEdit(mergedObject, CollectionReconciler.CollectInsertedIds(current, mergedObject));
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

    private static ApplyEditResponse CreateApplyEditResponse(ReconcileResult result, bool includeDocument)
    {
        return new ApplyEditResponse(
            result.Plan.Valid,
            result.Plan.Changes,
            result.Plan.Validation,
            result.Plan.ExpectedChangeSet,
            CreateCreatedIdSummary(result.Plan),
            includeDocument ? result.Document : null);
    }

    private static IReadOnlyList<AppliedEntityId> CreateCreatedIdSummary(ReconcilePlan plan)
    {
        var ids = new List<AppliedEntityId>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ChangeSetEntry change in plan.Changes)
        {
            if (change.Operation == ChangeOperations.InsertChild)
            {
                AddCreatedIds(change.NewValue, change.Path, ids, seen);
            }
        }

        return ids;
    }

    private static void AddCreatedIds(
        JsonNode? node,
        string path,
        List<AppliedEntityId> ids,
        HashSet<string> seen)
    {
        if (node is JsonObject obj)
        {
            string currentPath = path;
            if (TryReadObjectString(obj, nameof(CoreObject.Id)) is { } id)
            {
                string idSelector = $"[Id={id}]";
                currentPath = path.EndsWith(idSelector, StringComparison.Ordinal)
                    ? path
                    : $"{path}{idSelector}";
                if (seen.Add(id))
                {
                    ids.Add(new AppliedEntityId(
                        id,
                        currentPath,
                        TryReadObjectString(obj, "$type"),
                        TryReadObjectString(obj, nameof(CoreObject.Name))));
                }
            }

            foreach (KeyValuePair<string, JsonNode?> pair in obj.ToArray())
            {
                AddCreatedIds(pair.Value, $"{currentPath}/{pair.Key}", ids, seen);
            }
        }
        else if (node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                JsonNode? item = array[i];
                string itemPath = item is JsonObject itemObject
                                  && TryReadObjectString(itemObject, nameof(CoreObject.Id)) is { } id
                    ? $"{path}[Id={id}]"
                    : $"{path}[{i}]";
                AddCreatedIds(item, itemPath, ids, seen);
            }
        }
    }

    private static string? TryReadObjectString(JsonObject obj, string name)
    {
        return obj.TryGetPropertyValue(name, out JsonNode? node)
               && node is not null
               && node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : null;
    }

    private static bool EnforceFirstSelection(string? name, bool avoidRecent)
    {
        return avoidRecent && !string.IsNullOrWhiteSpace(name);
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
            "Call list_compositions to inspect options, then pass a returned name only when the user explicitly asked for a reusable template/starter. For original creative briefs, author a custom patch with plan_edit/apply_edit."));
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
            "expectedChangeSet must be the JSON array returned by plan_edit or plan_composition.",
            null,
            "Pass the object-shaped expectedChangeSet array directly. If a client exposes it as strings, pass either the whole JSON array string or one JSON object string per change entry."));
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
