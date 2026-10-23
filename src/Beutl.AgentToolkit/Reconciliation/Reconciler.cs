using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Reconciliation;

public sealed partial class Reconciler
{
    private static readonly HashSet<string> s_typedIdentityArrayNames = new(StringComparer.Ordinal)
    {
        "Elements",
        "Objects",
        "Children",
        "KeyFrames"
    };

    private static readonly HashSet<string> s_metadataPropertyNames = new(StringComparer.Ordinal)
    {
        "$type",
        "$delete",
        "$index",
        "$after",
        "$before",
        SchemaVersion.PropertyName,
        nameof(CoreObject.Id),
        nameof(CoreObject.Name),
        "Animations",
        nameof(EngineObject.Duration),
        "Expressions",
        nameof(EngineObject.Start),
        "Uri"
    };

    private static ReconcilePlan PlanPrepared(IEditingSession session, JsonObject desiredDocument, IReadOnlySet<Guid>? knownNewIds)
    {
        JsonObject currentDocument = session.Documents.Read(session.Root);
        ThrowIfMalformedTypedObjects(desiredDocument, currentDocument);

        HashSet<Guid> newIds = CollectionReconciler.MintMissingIds(
            desiredDocument,
            CollectionReconciler.CollectIds(currentDocument));
        if (knownNewIds is not null)
        {
            newIds.UnionWith(knownNewIds);
        }

        ThrowIfIdentityConflicts(currentDocument, desiredDocument, newIds);

        var validation = new List<ValidationOutcome>();
        CoreObject sandboxRoot = BuildValidationSandbox(session, currentDocument, desiredDocument);
        if (ExpandAnimationShorthand(sandboxRoot, desiredDocument))
        {
            newIds.UnionWith(CollectionReconciler.MintMissingIds(
                desiredDocument,
                CollectionReconciler.CollectIds(currentDocument)));
            ThrowIfMalformedTypedObjects(desiredDocument, currentDocument);
            ThrowIfIdentityConflicts(currentDocument, desiredDocument, newIds);

            // Use the same expanded, Id-complete document for validation, change reporting, and
            // eventual live application so plan/apply parity does not depend on synthetic $kf nodes.
            sandboxRoot = BuildValidationSandbox(session, currentDocument, desiredDocument);
        }

        ValidateChangedAnimationValues(sandboxRoot, currentDocument, desiredDocument, validation);

        var changes = new List<ChangeSetEntry>();
        CompareObject(session.Root, currentDocument, desiredDocument, "$", changes, validation);
        ThrowIfLockedChanges(session, sandboxRoot, currentDocument);
        ValidateInsertedSubtrees(sandboxRoot, changes, validation);
        ValidateChangedElementTimelines(sandboxRoot, changes, validation);
        ValidateSceneInvariants(sandboxRoot, validation);
        AddRelativeKeyFrameRangeWarnings(desiredDocument, validation);

        if (validation.FirstOrDefault(item => item.Status == ValidationStatus.Rejected) is { } rejected)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                rejected.Message ?? "Validation rejected the requested value.",
                null,
                rejected.Hint));
        }

        return new ReconcilePlan(changes, validation);
    }

    private static void ThrowIfMalformedTypedObjects(JsonObject desiredDocument, JsonObject currentDocument)
    {
        if (ValidateNewTypedObjectDiscriminators(desiredDocument, "$") is { } discriminatorError)
        {
            throw new ReconcileException(discriminatorError);
        }

        if (ValidateEngineObjectProperties(desiredDocument, "$", IndexObjectsById(currentDocument)) is { } propertyError)
        {
            throw new ReconcileException(propertyError);
        }
    }

    private static void ThrowIfIdentityConflicts(JsonObject currentDocument, JsonObject desiredDocument, HashSet<Guid> newIds)
    {
        // Ids already duplicated in the current document are tolerated so a
        // previously corrupted project stays editable and repairable.
        if (CollectionReconciler.ValidateNoDuplicateIdsInIdentityArrays(
                desiredDocument,
                CollectionReconciler.CollectDuplicatedIds(currentDocument)) is { } duplicateError)
        {
            throw new ReconcileException(duplicateError);
        }

        if (CollectionReconciler.ValidateIdentityReferences(currentDocument, desiredDocument, newIds) is { } referenceError)
        {
            throw new ReconcileException(referenceError);
        }
    }

    public ReconcileResult Apply(
        IEditingSession session,
        JsonObject desired,
        IReadOnlySet<Guid>? knownNewIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(desired);

        return session.EditOnSession(() => ApplyCore(session, desired, knownNewIds), cancellationToken);
    }

    // Read, merge, plan and mutate in one dispatch, so the live scene cannot change in between.
    public ReconcileResult ApplyFromCurrent(
        IEditingSession session,
        Func<JsonObject, (JsonObject Desired, IReadOnlySet<Guid>? KnownNewIds)> resolve,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(resolve);

        return session.EditOnSession(() =>
        {
            (JsonObject desired, IReadOnlySet<Guid>? knownNewIds) = resolve(session.Documents.Read(session.Root));
            return ApplyCore(session, desired, knownNewIds);
        }, cancellationToken);
    }

    // Resolve, validate the change set and mutate in one dispatch, so a UI edit cannot slip between
    // the check and the write.
    public ReconcileResult ApplyValidated(
        IEditingSession session,
        Func<JsonObject, (JsonObject Desired, IReadOnlySet<Guid>? KnownNewIds)> resolve,
        Func<ReconcilePlan, ToolError?> validate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(validate);

        return session.EditOnSession(() =>
        {
            (JsonObject desired, IReadOnlySet<Guid>? knownNewIds) = resolve(session.Documents.Read(session.Root));
            return ApplyCore(session, desired, knownNewIds, validate);
        }, cancellationToken);
    }

    private ReconcileResult ApplyCore(
        IEditingSession session,
        JsonObject desired,
        IReadOnlySet<Guid>? knownNewIds,
        Func<ReconcilePlan, ToolError?>? validate = null)
    {
        JsonObject desiredDocument = PrepareDesired(session, desired);
        ReconcilePlan plan = PlanPrepared(session, desiredDocument, knownNewIds);
        if (validate?.Invoke(plan) is { } error)
        {
            throw new ReconcileException(error);
        }

        Element[] affectedSuppressedElements = GetAffectedSuppressedElements(session.Root, plan);
        session.History.ExecuteInTransaction(
            () =>
            {
                session.Documents.Write(session.Root, desiredDocument);
                if (session.Root is Scene scene)
                {
                    ProjectOperations.NormalizeSidecarUrisWithinProject(scene);
                }

                foreach (Element element in affectedSuppressedElements)
                {
                    Beutl.Editor.Services.ElementRecoveryService.TryCompleteRepair(element, session.History);
                }
            },
            "Agent edit");

        session.MarkDirtyIfFileSession();

        return new ReconcileResult(plan, session.Documents.Read(session.Root));
    }

    private static Element[] GetAffectedSuppressedElements(CoreObject root, ReconcilePlan plan)
    {
        if (plan.Changes.Count == 0)
        {
            return [];
        }

        if (root is not Scene scene)
        {
            return [];
        }

        var affectedIds = new HashSet<Guid>();
        const string pathPrefix = "$/Elements[Id=";
        foreach (var change in plan.Changes)
        {
            if (Guid.TryParse(change.TargetId, out Guid targetId))
            {
                affectedIds.Add(targetId);
            }

            if (change.Path.StartsWith(pathPrefix, StringComparison.Ordinal))
            {
                int end = change.Path.IndexOf(']', pathPrefix.Length);
                if (end >= 0
                    && Guid.TryParse(change.Path.AsSpan(pathPrefix.Length, end - pathPrefix.Length), out Guid pathId))
                {
                    affectedIds.Add(pathId);
                }
            }
        }

        return scene.Children
            .Where(static child => child.SuppressedStorageSource is not null)
            .Where(child => affectedIds.Contains(child.Id.Value))
            .ToArray();
    }

    public ReconcilePlan PlanFromCurrent(
        IEditingSession session,
        Func<JsonObject, (JsonObject Desired, IReadOnlySet<Guid>? KnownNewIds)> resolve)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(resolve);

        return session.ReadOnSession(() =>
        {
            (JsonObject desired, IReadOnlySet<Guid>? knownNewIds) = resolve(session.Documents.Read(session.Root));
            return PlanPrepared(session, PrepareDesired(session, desired), knownNewIds);
        });
    }

    private static ToolError? ValidateNewTypedObjectDiscriminators(JsonNode? node, string path)
    {
        if (node is JsonObject obj)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in obj.ToArray())
            {
                string childPath = $"{path}/{pair.Key}";
                if (pair.Value is JsonArray array && s_typedIdentityArrayNames.Contains(pair.Key))
                {
                    for (int i = 0; i < array.Count; i++)
                    {
                        if (array[i] is not JsonObject item || IsDeletionMarker(item) || CollectionReconciler.TryGetId(item, out _))
                        {
                            continue;
                        }

                        if (!item.ContainsKey("$type"))
                        {
                            string itemPath = $"{childPath}[{i}]";
                            string hint = pair.Key == "Elements"
                                ? "Add '$type': '[Beutl.ProjectSystem]:Element' for new timeline elements. Existing elements keep their Id; genuinely new Elements omit Id so the toolkit can mint one."
                                : "Add the concrete '$type' discriminator returned by get_schema for new objects in polymorphic arrays such as Objects, Children, and KeyFrames.";
                            return new ToolError(
                                ErrorCode.ValidationRejected,
                                $"New typed object at '{itemPath}' is missing the '$type' discriminator.",
                                itemPath,
                                hint);
                        }
                    }
                }

                if (ValidateNewTypedObjectDiscriminators(pair.Value, childPath) is { } childError)
                {
                    return childError;
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (ValidateNewTypedObjectDiscriminators(array[i], $"{path}[{i}]") is { } childError)
                {
                    return childError;
                }
            }
        }

        return null;
    }

    // template is what a new instance of the owner writes in this place, such as a port that a new node creates.
    private static ToolError? ValidateEngineObjectProperties(
        JsonNode? node, string path, Dictionary<Guid, JsonObject> current, JsonNode? template = null)
    {
        if (node is JsonObject obj)
        {
            JsonObject? created = template is JsonObject candidate && Discriminator(candidate) == Discriminator(obj)
                ? candidate
                : null;
            if (obj.TryGetDiscriminator(out Type? type) && typeof(EngineObject).IsAssignableFrom(type))
            {
                (HashSet<string> Names, JsonObject Json)? instance = CreateNewInstanceMembers(type);
                // Such as the value of a port, which a new port does not have.
                JsonObject? same = FindCurrentObject(obj, current) ?? created;
                if (instance is not null || same is not null)
                {
                    foreach (KeyValuePair<string, JsonNode?> pair in obj.ToArray())
                    {
                        if (s_metadataPropertyNames.Contains(pair.Key)
                            || instance?.Names.Contains(pair.Key) == true
                            || same?.ContainsKey(pair.Key) == true)
                        {
                            continue;
                        }

                        string propertyPath = $"{path}/{pair.Key}";
                        return new ToolError(
                            ErrorCode.ValidationRejected,
                            $"Property '{pair.Key}' is not supported by '{type.Name}'.",
                            propertyPath,
                            $"Call get_schema for '{type.Name}' and use only the returned PascalCase property names.");
                    }
                }

                created ??= instance?.Json;
            }

            foreach (KeyValuePair<string, JsonNode?> pair in obj.ToArray())
            {
                if (ValidateEngineObjectProperties(pair.Value, $"{path}/{pair.Key}", current, created?[pair.Key]) is { } childError)
                {
                    return childError;
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (ValidateEngineObjectProperties(array[i], $"{path}[{i}]", current, FindNamedItem(template as JsonArray, array[i]))
                    is { } childError)
                {
                    return childError;
                }
            }
        }

        return null;
    }

    // The members of a new instance, and its JSON; null for a type without a public parameterless constructor.
    private static (HashSet<string> Names, JsonObject Json)? CreateNewInstanceMembers(Type type)
    {
        if (type.GetConstructor(Type.EmptyTypes) is null || Activator.CreateInstance(type) is not EngineObject engineObject)
        {
            return null;
        }

        JsonObject json = CoreSerializer.SerializeToJsonObject(
            engineObject, new CoreSerializerOptions { Mode = CoreSerializationMode.EmbedReferencedObjects });
        var names = new HashSet<string>(engineObject.Properties.Select(property => property.Name), StringComparer.Ordinal);
        names.UnionWith(json.Select(pair => pair.Key));
        return (names, json);
    }

    // The item of templates with the Name of item, as a node reads each of its ports by name.
    private static JsonObject? FindNamedItem(JsonArray? templates, JsonNode? item)
    {
        if (templates is null || item is not JsonObject obj || Name(obj) is not { } name)
        {
            return null;
        }

        return templates.OfType<JsonObject>().FirstOrDefault(candidate => Name(candidate) == name);

        static string? Name(JsonObject obj) => obj["Name"] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
    }

    // The objects of the document by Id, the first one where an Id is written twice.
    private static Dictionary<Guid, JsonObject> IndexObjectsById(JsonObject document)
    {
        var objects = new Dictionary<Guid, JsonObject>();
        Index(document);
        return objects;

        void Index(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                if (CollectionReconciler.TryGetId(obj, out Guid id))
                {
                    objects.TryAdd(id, obj);
                }

                foreach (KeyValuePair<string, JsonNode?> pair in obj)
                {
                    Index(pair.Value);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    Index(item);
                }
            }
        }
    }

    // The object of the current document with the Id and type of obj.
    private static JsonObject? FindCurrentObject(JsonObject obj, Dictionary<Guid, JsonObject> current)
    {
        return CollectionReconciler.TryGetId(obj, out Guid id)
               && current.TryGetValue(id, out JsonObject? found)
               && Discriminator(found) == Discriminator(obj)
            ? found
            : null;
    }

    private static string? Discriminator(JsonObject obj)
        => obj["$type"] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static bool IsDeletionMarker(JsonObject obj)
    {
        return obj.TryGetPropertyValue("$delete", out JsonNode? deleteNode)
               && deleteNode?.GetValueKind() == JsonValueKind.True;
    }

    private static JsonObject PrepareDesired(IEditingSession session, JsonObject desired)
    {
        JsonObject document = (JsonObject)desired.DeepClone();
        SchemaVersion.EnsureKnown(document);

        if (!document.ContainsKey(nameof(CoreObject.Id)))
        {
            document[nameof(CoreObject.Id)] = session.Root.Id.Value.ToString();
        }

        ValidateReferenceValues(session.Root, document);
        return document;
    }
}
