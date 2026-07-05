using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.ProjectSystem;
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
    IReadOnlyDictionary<string, int> Operations,
    int ChangeCount,
    IReadOnlyDictionary<string, int> ValidationStatuses,
    int ValidationCount,
    IReadOnlyList<AppliedEntityId> CreatedIds,
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
public sealed class EditTools(AgentSessionManager sessions) : ToolBase
{
    private readonly Reconciler _reconciler = new();
    private readonly CompositionTemplateCatalog _compositionCatalog = new();

    [McpServerTool(Name = "apply_edit")]
    [Description("Atomically applies a declarative desired document or JSON Merge Patch through Beutl history. In the in-app host, call attach_active_editor first. Supply exactly one of desired or patch; prefer patch for targeted edits. For file-backed sessions, call save_project after each major successful apply_edit. The response is compact by default: appliedChangeSet plus createdIds for follow-up edits. Set includeDocument=true only when you need the full updated document.")]
    public ToolResult<ApplyEditResponse> ApplyEdit(
        [Description("Full desired declarative document. Uses PascalCase properties, $type discriminators, stable Id fields, typed properties for transforms/geometry/pens/brushes/effects, Animations.<Property>.KeyFrames for keyframes, and schemaVersion. Full desired documents are authoritative: omitted child arrays such as Elements or Objects can delete existing content, so use patch for partial edits.")]
        JsonObject? desired = null,
        [Description("JSON Merge Patch. Objects follow RFC 7396; Id-bearing arrays such as Elements, Objects, GradientStops, transform/effect Children, audio effect Children, and KeyFrames are merged by Id. Omit Id to insert; use {Id,$delete:true} to delete; use $index/$after/$before to reorder non-keyframe arrays. Unmentioned siblings are preserved. To wholesale-replace an Id-bearing array (e.g. swap a FilterEffectGroup.Children chain) instead of merging into it, make the FIRST array element the sentinel {\"$replace\":true}; the remaining elements rebuild the array in order (they may omit Id to be minted fresh, or reuse an Id to keep that child), and an array of just [{\"$replace\":true}] clears it. Replacement elements cannot also carry $delete or $index/$after/$before.")]
        JsonObject? patch = null,
        [Description("Declarative document schema version. Required for patch; for desired, pass it here or include schemaVersion in the document. Mismatches are rejected instead of silently dropping content.")]
        string? schemaVersion = null,
        [Description("Return the full updated document. Defaults to false to keep apply_edit responses compact; prefer createdIds or read_document_summary for follow-up edits.")]
        bool includeDocument = false,
        [Description("When true, return only validity, operation/validation counts, and createdIds. Set false when you need detailed changes, validation, or appliedChangeSet.")]
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
            if (CompositionTemplateCatalog.TryInferTemplateName(patch ?? desired!) is { } inferredName)
            {
                sessions.RecordCompositionUse(inferredName);
            }

            return CreateApplyEditResponse(result, includeDocument, quiet);
        });
    }

    [McpServerTool(Name = "duplicate_object")]
    [Description("Duplicates one EngineObject (e.g. a Drawable) within its owning timeline Element.Objects, minting fresh Ids on every nested node, and returns the new object's Id. The copy is appended after the original (front-most within the Element), so applying an additive-blend look to the returned objectId layers an emissive glow over the untouched original — see get_effect_recipe \"additive-bloom\". Pass wrapInGroup=true for additive bloom so the original and copy live under a DrawableGroup IFlowOperator and evaluate_edit_quality stays gate-clean; duplicate then move the copy to a separate Element when you need independent timing or z-order.")]
    public ToolResult<DuplicateObjectResponse> DuplicateObject(
        [Description("Id of the object to duplicate. Must be an object inside some timeline Element.Objects (e.g. a drawable).")]
        string objectId,
        [Description("Optional owning Element Id to scope the search; omit to search every element for objectId.")]
        string? elementId = null,
        [Description("When true, wrap the original drawable and the new copy in a DrawableGroup inside the same Element. If the source drawable is already inside a DrawableGroup, the copy is appended to that existing group instead.")]
        bool wrapInGroup = false)
    {
        return Execute(() =>
        {
            IEditingSession session = sessions.RequireSession();
            if (wrapInGroup)
            {
                // Locate/read/clone the source and mutate all on the session dispatcher so a
                // LiveEditor wrap does not traverse the UI-owned scene on the MCP request thread.
                return session.ReadOnSession(() => DuplicateObjectIntoDrawableGroup(session, objectId, elementId));
            }

            // Read the source document, clone, and plan/apply all inside the reconcile dispatch so a
            // LiveEditor duplicate does not read the UI-owned scene on the MCP request thread.
            string elementId2 = string.Empty;
            var newId = Guid.NewGuid();
            ReconcileResult result = _reconciler.ApplyFromCurrent(session, desired =>
            {
                (JsonObject element, JsonArray objects, JsonObject source) = FindObjectInElements(desired, objectId, elementId);
                var clone = (JsonObject)source.DeepClone();
                RemoveIds(clone);
                clone[nameof(CoreObject.Id)] = newId.ToString();
                objects.Add(clone);
                elementId2 = ReadId(element) ?? string.Empty;
                return (desired, new HashSet<Guid> { newId });
            });

            return new DuplicateObjectResponse(
                result.Plan.Valid,
                elementId2,
                newId.ToString(),
                CreateCreatedIdSummary(result.Plan));
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
            // Resolve the patch and plan inside the session dispatch so a LiveEditor plan does not
            // read the UI-owned scene on the MCP request thread.
            ResolvedEdit resolved = null!;
            ReconcilePlan plan = _reconciler.PlanFromCurrent(session, current =>
            {
                resolved = ResolvePatchEdit(current, composition.Patch, SchemaVersion.Current);
                return (resolved.Document, resolved.KnownNewIds);
            });
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
            JsonArray? normalizedExpectedChangeSet = NormalizeExpectedChangeSet(expectedChangeSet);
            ReconcileResult result = _reconciler.ApplyValidated(
                session,
                current =>
                {
                    ResolvedEdit resolved = ResolvePatchEdit(current, composition.Patch, SchemaVersion.Current);
                    return (resolved.Document, resolved.KnownNewIds);
                },
                plan => normalizedExpectedChangeSet is null || ChangeSetMatches(plan, normalizedExpectedChangeSet)
                    ? null
                    : new ToolError(
                        ErrorCode.ValidationRejected,
                        "The live composition change set differs from expectedChangeSet.",
                        null,
                        "Run plan_composition again and submit the updated expectedChangeSet."));
            sessions.RecordCompositionUse(composition.Name);
            return new ApplyCompositionResponse(
                SchemaVersion.Current,
                CreateCompositionRunSummary(composition),
                null,
                result);
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

    private static ApplyEditResponse CreateApplyEditResponse(ReconcileResult result, bool includeDocument, bool quiet)
    {
        return new ApplyEditResponse(
            result.Plan.Valid,
            result.Plan.Operations,
            result.Plan.ChangeCount,
            result.Plan.ValidationStatuses,
            result.Plan.ValidationCount,
            CreateCreatedIdSummary(result.Plan),
            quiet ? null : result.Plan.Changes,
            quiet ? null : result.Plan.Validation,
            quiet ? null : result.Plan.ExpectedChangeSet,
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

    private static DuplicateObjectResponse DuplicateObjectIntoDrawableGroup(
        IEditingSession session,
        string objectId,
        string? elementId)
    {
        if (session.Root is not Scene scene)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "duplicate_object wrapInGroup requires a scene editing session.",
                objectId));
        }

        if (!Guid.TryParse(objectId, out Guid sourceId))
        {
            throw StaleObject(objectId);
        }

        Guid? scopedElementId = null;
        if (!string.IsNullOrWhiteSpace(elementId))
        {
            if (!Guid.TryParse(elementId, out Guid parsedElementId))
            {
                throw StaleObject(objectId);
            }

            scopedElementId = parsedElementId;
        }

        DuplicateObjectLocation location = FindObjectLocation(scene, sourceId, scopedElementId, objectId);
        if (location.Source is not Drawable source)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Object '{objectId}' is not a Drawable and cannot be wrapped in a DrawableGroup.",
                objectId,
                "Use wrapInGroup only for drawable objects such as TextBlock, RectShape, SourceImage, or SourceVideo."));
        }

        JsonObject sourceJson = FindObjectJsonInElements(session.Documents.Read(session.Root), objectId, elementId);
        var newId = Guid.NewGuid();
        Drawable clone = CloneDrawable(sourceJson, newId, session.Root);
        DrawableGroup group;
        PortalObject? portal = null;

        void Mutate()
        {
            if (location.ParentGroup is { } parentGroup)
            {
                group = parentGroup;
                parentGroup.Children.Add(clone);
                return;
            }

            int sourceIndex = location.Element.Objects.IndexOf(location.Source);
            if (sourceIndex < 0)
            {
                throw StaleObject(objectId);
            }

            group = new DrawableGroup
            {
                Name = string.IsNullOrWhiteSpace(source.Name)
                    ? "Drawable bloom group"
                    : $"{source.Name} bloom group"
            };

            location.Element.InsertObject(sourceIndex, group);
            portal = location.Element.Objects[sourceIndex] as PortalObject;
            location.Element.Objects.Remove(location.Source);
            group.Children.Add(source);
            group.Children.Add(clone);
        }

        group = null!;
        ExecuteInSessionTransaction(session, Mutate, "Agent duplicate object");

        var createdIds = new List<AppliedEntityId>();
        if (portal is not null)
        {
            createdIds.Add(CreateAppliedEntityId(
                portal,
                $"$/Elements[Id={location.Element.Id}]/Objects[Id={portal.Id}]"));
        }

        if (location.ParentGroup is null)
        {
            createdIds.Add(CreateAppliedEntityId(
                group,
                $"$/Elements[Id={location.Element.Id}]/Objects[Id={group.Id}]"));
        }

        createdIds.Add(CreateAppliedEntityId(
            clone,
            $"$/Elements[Id={location.Element.Id}]/Objects[Id={group.Id}]/Children[Id={clone.Id}]"));

        return new DuplicateObjectResponse(
            true,
            location.Element.Id.ToString(),
            clone.Id.ToString(),
            createdIds,
            group.Id.ToString());
    }

    private static void ExecuteInSessionTransaction(IEditingSession session, Action mutate, string name)
    {
        void Execute() => session.History.ExecuteInTransaction(mutate, name);

        if (session is IEditingSessionDispatcher dispatcher)
        {
            dispatcher.Invoke(Execute);
        }
        else
        {
            Execute();
        }

        if (session is FileEditingSession fileSession)
        {
            fileSession.MarkDirty();
        }
    }

    private static Drawable CloneDrawable(JsonObject sourceJson, Guid newId, CoreObject root)
    {
        var cloneJson = (JsonObject)sourceJson.DeepClone();
        RemoveIds(cloneJson);
        cloneJson[nameof(CoreObject.Id)] = newId.ToString();

        return (Drawable)CoreSerializer.DeserializeFromJsonObject(
            cloneJson,
            typeof(Drawable),
            new CoreSerializerOptions
            {
                BaseUri = root.Uri,
                Mode = CoreSerializationMode.Read | CoreSerializationMode.EmbedReferencedObjects
            });
    }

    private static AppliedEntityId CreateAppliedEntityId(CoreObject obj, string path)
    {
        return new AppliedEntityId(
            obj.Id.ToString(),
            path,
            IdentityHelper.WriteDiscriminator(obj.GetType()),
            string.IsNullOrWhiteSpace(obj.Name) ? null : obj.Name);
    }

    private static DuplicateObjectLocation FindObjectLocation(
        Scene scene,
        Guid objectId,
        Guid? elementId,
        string objectIdText)
    {
        foreach (Element element in scene.Children)
        {
            if (elementId is not null && element.Id != elementId)
            {
                continue;
            }

            foreach (EngineObject obj in element.Objects)
            {
                if (obj.Id == objectId)
                {
                    return new DuplicateObjectLocation(element, obj, null);
                }

                if (obj is DrawableGroup group
                    && TryFindObjectInDrawableGroup(element, group, objectId, out DuplicateObjectLocation? location))
                {
                    return location;
                }
            }
        }

        throw StaleObject(objectIdText);
    }

    private static bool TryFindObjectInDrawableGroup(
        Element element,
        DrawableGroup group,
        Guid objectId,
        [NotNullWhen(true)] out DuplicateObjectLocation? location)
    {
        foreach (Drawable child in group.Children)
        {
            if (child.Id == objectId)
            {
                location = new DuplicateObjectLocation(element, child, group);
                return true;
            }

            if (child is DrawableGroup childGroup
                && TryFindObjectInDrawableGroup(element, childGroup, objectId, out location))
            {
                return true;
            }
        }

        location = null;
        return false;
    }

    private static JsonObject FindObjectJsonInElements(
        JsonObject document,
        string objectId,
        string? elementId)
    {
        if (document["Elements"] is not JsonArray elements)
        {
            throw new InvalidOperationException("The current scene document does not contain an Elements array.");
        }

        foreach (JsonNode? elementNode in elements)
        {
            if (elementNode is not JsonObject element
                || (elementId is not null && ReadId(element) != elementId)
                || element["Objects"] is not JsonArray objects)
            {
                continue;
            }

            if (FindObjectJson(objects, objectId) is { } source)
            {
                return source;
            }
        }

        throw StaleObject(objectId);
    }

    private static JsonObject? FindObjectJson(JsonNode? node, string objectId)
    {
        if (node is JsonObject obj)
        {
            if (ReadId(obj) == objectId)
            {
                return obj;
            }

            foreach (JsonNode? child in obj.Select(pair => pair.Value).ToArray())
            {
                if (FindObjectJson(child, objectId) is { } found)
                {
                    return found;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array.ToArray())
            {
                if (FindObjectJson(child, objectId) is { } found)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static (JsonObject Element, JsonArray Objects, JsonObject Source) FindObjectInElements(
        JsonObject document,
        string objectId,
        string? elementId)
    {
        if (document["Elements"] is not JsonArray elements)
        {
            throw new InvalidOperationException("The current scene document does not contain an Elements array.");
        }

        foreach (JsonNode? elementNode in elements)
        {
            if (elementNode is not JsonObject element
                || (elementId is not null && ReadId(element) != elementId)
                || element["Objects"] is not JsonArray objects)
            {
                continue;
            }

            foreach (JsonNode? objectNode in objects)
            {
                if (objectNode is JsonObject source && ReadId(source) == objectId)
                {
                    return (element, objects, source);
                }
            }
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.StaleHandle,
            $"No object with Id '{objectId}' exists in a timeline element.",
            objectId));
    }

    private static ReconcileException StaleObject(string objectId)
    {
        return new ReconcileException(new ToolError(
            ErrorCode.StaleHandle,
            $"No object with Id '{objectId}' exists in a timeline element.",
            objectId));
    }

    private static string? ReadId(JsonObject obj)
    {
        return obj.TryGetPropertyValue(nameof(CoreObject.Id), out JsonNode? node)
            ? node?.GetValue<string>()
            : null;
    }

    private static void RemoveIds(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove(nameof(CoreObject.Id));
            foreach (JsonNode? child in obj.Select(pair => pair.Value).ToArray())
            {
                RemoveIds(child);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array.ToArray())
            {
                RemoveIds(child);
            }
        }
    }
}
