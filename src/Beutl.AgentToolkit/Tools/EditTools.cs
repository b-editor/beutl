using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.ProjectSystem;
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
    IReadOnlyDictionary<string, int> Operations,
    int ChangeCount,
    IReadOnlyDictionary<string, int> ValidationStatuses,
    int ValidationCount,
    IReadOnlyList<AppliedEntityId> CreatedIds,
    int CreatedIdCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ChangeSetEntry>? Changes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ValidationOutcome>? Validation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JsonArray? AppliedChangeSet = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JsonObject? Document = null);

public sealed record DuplicateObjectResponse(
    bool Valid,
    string ElementId,
    string ObjectId,
    IReadOnlyList<AppliedEntityId> CreatedIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? GroupId = null);

internal sealed record DuplicateObjectLocation(
    Element Element,
    EngineObject Source,
    DrawableGroup? ParentGroup);

internal sealed record ResolvedEdit(JsonObject Document, HashSet<Guid> KnownNewIds);

[McpServerToolType]
public sealed partial class EditTools(AgentSessionManager sessions) : ToolBase
{
    private readonly Reconciler _reconciler = new();
    private readonly CompositionTemplateCatalog _compositionCatalog = new();

    [McpServerTool(Name = "apply_edit")]
    [Description("Atomically applies a declarative desired document or JSON Merge Patch through Beutl history. In the in-app host, call attach_active_editor first. Supply exactly one of desired or patch; prefer patch for targeted edits. For file-backed sessions, call save_project after each major successful apply_edit. The response is compact by default: appliedChangeSet plus createdIds for follow-up edits. createdIds lists only entities you can address later — animations and keyframes are reached through their owning property, never by Id, so they are excluded; pass quiet:true to narrow it further to named entities. createdIdCount always carries the true total. Set includeDocument=true only when you need the full updated document. Keyframes accept a shorthand: Animations.<Property> = {\"$kf\": [[seconds, value, easing?], ...]} or [{\"t\": seconds, \"v\": value, \"easing\": name}, ...]. The animation and keyframe $type discriminators are inferred from the property's value type and the easing takes a bare name such as CubicEaseOut, so a four-key envelope costs a line instead of ~800 characters. Sibling members (UseGlobalClock, Id) still apply, and the long explicit form keeps working.")]
    public ToolResult<ApplyEditResponse> ApplyEdit(
        [Description("Full desired declarative document. Uses PascalCase properties, $type discriminators, stable Id fields, typed properties for transforms/geometry/pens/brushes/effects, Animations.<Property>.KeyFrames for keyframes, and schemaVersion. Full desired documents are authoritative: omitted child arrays such as Elements or Objects can delete existing content, so use patch for partial edits.")]
        JsonObject? desired = null,
        [Description("JSON Merge Patch. Objects follow RFC 7396; Id-bearing arrays such as Elements, Objects, GradientStops, transform/effect Children, audio effect Children, and KeyFrames are merged by Id. Omit Id to insert; use {Id,$delete:true} to delete; use $index/$after/$before to reorder non-keyframe arrays. Unmentioned siblings are preserved. To wholesale-replace an Id-bearing array (e.g. swap a FilterEffectGroup.Children chain) instead of merging into it, make the FIRST array element the sentinel {\"$replace\":true}; the remaining elements rebuild the array in order (they may omit Id to be minted fresh, or reuse an Id to keep that child), and an array of just [{\"$replace\":true}] clears it. Replacement elements cannot also carry $delete or $index/$after/$before.")]
        JsonObject? patch = null,
        [Description("Declarative document schema version. Required for patch; for desired, pass it here or include schemaVersion in the document. Mismatches are rejected instead of silently dropping content.")]
        string? schemaVersion = null,
        [Description("Return the full updated document. Defaults to false to keep apply_edit responses compact; prefer createdIds or read_document_summary for follow-up edits.")]
        bool includeDocument = false,
        [Description("When true, return only validity, operation/validation counts, and the named entries of createdIds. Set false when you need detailed changes, validation, or appliedChangeSet. createdIdCount always reports how many entities were created, so a trimmed list never hides the total.")]
        bool quiet = false)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            RequireExactlyOneEdit(desired, patch);
            ReconcileResult result = desired is not null
                ? _reconciler.Apply(session, ResolveDesiredEdit(desired, schemaVersion))
                // Resolve the patch inside the reconcile dispatch so the read + merge is atomic with
                // the mutation on a live session (see Reconciler.ApplyFromCurrent).
                : _reconciler.ApplyFromCurrent(session, current =>
                {
                    ResolvedEdit resolved = ResolvePatchEdit(current, patch!, schemaVersion);
                    return (resolved.Document, resolved.KnownNewIds);
                });

            return CreateApplyEditResponse(result, includeDocument, quiet);
        });
    }

    private static void RequireExactlyOneEdit(JsonObject? desired, JsonObject? patch)
    {
        if ((desired is null) == (patch is null))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "Supply exactly one of desired or patch."));
        }
    }

    private static JsonObject ResolveDesiredEdit(JsonObject desired, string? schemaVersion)
    {
        JsonObject document = (JsonObject)desired.DeepClone();
        if (schemaVersion is not null)
        {
            document[SchemaVersion.PropertyName] = schemaVersion;
        }

        return document;
    }

    private static ResolvedEdit ResolvePatchEdit(JsonObject current, JsonObject patch, string? schemaVersion)
    {
        SchemaVersion.EnsureKnown(schemaVersion);
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

    private static ApplyEditResponse CreateApplyEditResponse(ReconcileResult result, bool includeDocument, bool quiet)
    {
        IReadOnlyList<AppliedEntityId> created = CreateCreatedIdSummary(result.Plan);
        return new ApplyEditResponse(
            result.Plan.Valid,
            result.Plan.Operations,
            result.Plan.ChangeCount,
            result.Plan.ValidationStatuses,
            result.Plan.ValidationCount,
            quiet ? created.Where(item => !string.IsNullOrWhiteSpace(item.Name)).ToArray() : created,
            created.Count,
            quiet ? null : result.Plan.Changes,
            quiet ? null : result.Plan.Validation,
            quiet ? null : result.Plan.ExpectedChangeSet,
            includeDocument ? result.Document : null);
    }

    private static List<AppliedEntityId> CreateCreatedIdSummary(ReconcilePlan plan)
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
                // Animations and their keyframes carry Ids, but nothing addresses them by Id later —
                // they are reached through the owning property — so listing them is pure payload.
                if (pair.Key is "Animations" or nameof(KeyFrameAnimation.KeyFrames))
                {
                    continue;
                }

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
}
