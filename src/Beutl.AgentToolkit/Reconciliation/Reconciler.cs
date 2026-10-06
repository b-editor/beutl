using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Documents;
using Beutl.AgentToolkit.Sessions;
using Beutl.Animation;
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

    public ReconcilePlan Plan(IEditingSession session, JsonObject desired, IReadOnlySet<Guid>? knownNewIds = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(desired);

        JsonObject desiredDocument = PrepareDesired(session, desired);
        return PlanPrepared(session, desiredDocument, knownNewIds);
    }

    private static ReconcilePlan PlanPrepared(IEditingSession session, JsonObject desiredDocument, IReadOnlySet<Guid>? knownNewIds)
    {
        JsonObject currentDocument = session.Documents.Read(session.Root);
        if (ValidateNewTypedObjectDiscriminators(desiredDocument, "$") is { } discriminatorError)
        {
            throw new ReconcileException(discriminatorError);
        }

        if (ValidateEngineObjectProperties(desiredDocument, "$") is { } propertyError)
        {
            throw new ReconcileException(propertyError);
        }

        HashSet<Guid> newIds = CollectionReconciler.MintMissingIds(
            desiredDocument,
            CollectionReconciler.CollectIds(currentDocument));
        if (knownNewIds is not null)
        {
            newIds.UnionWith(knownNewIds);
        }

        // Ids already duplicated in the current document are tolerated so a
        // previously corrupted project stays editable and repairable.
        if (CollectionReconciler.ValidateNoDuplicateIdsInIdentityArrays(
                desiredDocument,
                CollectionReconciler.CollectDuplicatedIds(currentDocument)) is { } duplicateError)
        {
            throw new ReconcileException(duplicateError);
        }

        if (CollectionReconciler.ValidateIdentityReferences(currentDocument, desiredDocument, newIds) is { } error)
        {
            throw new ReconcileException(error);
        }

        var validation = new List<ValidationOutcome>();
        CoreObject sandboxRoot = BuildValidationSandbox(session, currentDocument, desiredDocument);
        if (ExpandAnimationShorthand(sandboxRoot, desiredDocument))
        {
            newIds.UnionWith(CollectionReconciler.MintMissingIds(
                desiredDocument,
                CollectionReconciler.CollectIds(currentDocument)));
            if (ValidateNewTypedObjectDiscriminators(desiredDocument, "$") is { } expandedDiscriminatorError)
            {
                throw new ReconcileException(expandedDiscriminatorError);
            }

            if (ValidateEngineObjectProperties(desiredDocument, "$") is { } expandedPropertyError)
            {
                throw new ReconcileException(expandedPropertyError);
            }

            if (CollectionReconciler.ValidateNoDuplicateIdsInIdentityArrays(
                    desiredDocument,
                    CollectionReconciler.CollectDuplicatedIds(currentDocument)) is { } expandedDuplicateError)
            {
                throw new ReconcileException(expandedDuplicateError);
            }

            if (CollectionReconciler.ValidateIdentityReferences(
                    currentDocument,
                    desiredDocument,
                    newIds) is { } expandedReferenceError)
            {
                throw new ReconcileException(expandedReferenceError);
            }

            // Use the same expanded, Id-complete document for validation, change reporting, and
            // eventual live application so plan/apply parity does not depend on synthetic $kf nodes.
            sandboxRoot = BuildValidationSandbox(session, currentDocument, desiredDocument);
        }

        ValidateChangedAnimationValues(sandboxRoot, currentDocument, desiredDocument, validation);

        var changes = new List<ChangeSetEntry>();
        CompareObject(session.Root, currentDocument, desiredDocument, "$", changes, validation);
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

    public ReconcileResult Apply(IEditingSession session, JsonObject desired, IReadOnlySet<Guid>? knownNewIds = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(desired);

        return Dispatch(session, () => ApplyCore(session, desired, knownNewIds));
    }

    // Resolve the desired document from the current one INSIDE the mutation dispatch, so a patch's
    // read + merge + plan + mutate is one atomic operation on the editor thread — no torn read of the
    // live scene, and a single dispatch (not one for the read and another for the write).
    public ReconcileResult ApplyFromCurrent(
        IEditingSession session,
        Func<JsonObject, (JsonObject Desired, IReadOnlySet<Guid>? KnownNewIds)> resolve)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(resolve);

        return Dispatch(session, () =>
        {
            (JsonObject desired, IReadOnlySet<Guid>? knownNewIds) = resolve(session.Documents.Read(session.Root));
            return ApplyCore(session, desired, knownNewIds);
        });
    }

    // Resolve, change-set-validate, and mutate in ONE dispatch, so a concurrent UI edit cannot change
    // the live scene between the check and the write — the validated plan is built from the read Apply
    // then consumes.
    public ReconcileResult ApplyValidated(
        IEditingSession session,
        Func<JsonObject, (JsonObject Desired, IReadOnlySet<Guid>? KnownNewIds)> resolve,
        Func<ReconcilePlan, ToolError?> validate)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(validate);

        return Dispatch(session, () =>
        {
            (JsonObject desired, IReadOnlySet<Guid>? knownNewIds) = resolve(session.Documents.Read(session.Root));
            ReconcilePlan plan = PlanPrepared(session, PrepareDesired(session, desired), knownNewIds);
            if (validate(plan) is { } error)
            {
                throw new ReconcileException(error);
            }

            return ApplyCore(session, desired, knownNewIds);
        });
    }

    private ReconcileResult ApplyCore(IEditingSession session, JsonObject desired, IReadOnlySet<Guid>? knownNewIds)
    {
        JsonObject desiredDocument = PrepareDesired(session, desired);
        ReconcilePlan plan = PlanPrepared(session, desiredDocument, knownNewIds);
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

        if (session is FileEditingSession fileSession)
        {
            fileSession.MarkDirty();
        }

        return new ReconcileResult(plan, session.Documents.Read(session.Root));
    }

    private static Element[] GetAffectedSuppressedElements(CoreObject root, ReconcilePlan plan)
    {
        if (plan.Changes.Count == 0)
        {
            return [];
        }

        if (root is Element { SuppressedStorageSource: not null } element)
        {
            return [element];
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
            .Where(child => affectedIds.Contains(child.Id))
            .ToArray();
    }

    // Build the plan on the editor's dispatcher: PlanPrepared reads session.Documents/Root, so off
    // the MCP request thread it would race the live scene the editor mutates on the UI thread.
    public ReconcilePlan PlanFromCurrent(
        IEditingSession session,
        Func<JsonObject, (JsonObject Desired, IReadOnlySet<Guid>? KnownNewIds)> resolve)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(resolve);

        return Dispatch(session, () =>
        {
            (JsonObject desired, IReadOnlySet<Guid>? knownNewIds) = resolve(session.Documents.Read(session.Root));
            return PlanPrepared(session, PrepareDesired(session, desired), knownNewIds);
        });
    }

    // Plan and mutate on the editor's dispatcher: reading session.Root/Documents off the MCP request
    // thread would race the live scene the editor mutates on the UI thread. File sessions run inline.
    private static T Dispatch<T>(IEditingSession session, Func<T> core)
    {
        if (session is IEditingSessionDispatcher dispatcher)
        {
            T result = default!;
            dispatcher.Invoke(() => result = core());
            return result;
        }

        return core();
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

    private static ToolError? ValidateEngineObjectProperties(JsonNode? node, string path)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetDiscriminator(out Type? type) && type is not null && typeof(EngineObject).IsAssignableFrom(type))
            {
                HashSet<string> allowed = CreateAllowedEngineObjectPropertyNames(type);
                foreach (KeyValuePair<string, JsonNode?> pair in obj.ToArray())
                {
                    if (allowed.Contains(pair.Key))
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

            foreach (KeyValuePair<string, JsonNode?> pair in obj.ToArray())
            {
                if (ValidateEngineObjectProperties(pair.Value, $"{path}/{pair.Key}") is { } childError)
                {
                    return childError;
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (ValidateEngineObjectProperties(array[i], $"{path}[{i}]") is { } childError)
                {
                    return childError;
                }
            }
        }

        return null;
    }

    private static HashSet<string> CreateAllowedEngineObjectPropertyNames(Type type)
    {
        var allowed = new HashSet<string>(s_metadataPropertyNames, StringComparer.Ordinal);
        foreach (CoreProperty property in PropertyRegistry.GetRegistered(type))
        {
            allowed.Add(property.Name);
        }

        if (Activator.CreateInstance(type) is EngineObject engineObject)
        {
            foreach (IProperty property in engineObject.Properties)
            {
                allowed.Add(property.Name);
            }

            var serializerOptions = new CoreSerializerOptions
            {
                Mode = CoreSerializationMode.EmbedReferencedObjects
            };
            foreach (KeyValuePair<string, JsonNode?> pair in CoreSerializer.SerializeToJsonObject(engineObject, serializerOptions))
            {
                allowed.Add(pair.Key);
            }
        }

        return allowed;
    }

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
            document[nameof(CoreObject.Id)] = session.Root.Id.ToString();
        }

        ValidateReferenceValues(session.Root, document);
        return document;
    }
}
