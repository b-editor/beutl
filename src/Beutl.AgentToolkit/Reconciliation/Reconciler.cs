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

public sealed class Reconciler
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

        CoreObject sandboxRoot = BuildValidationSandbox(session, currentDocument, desiredDocument);
        ValidateNoNewFallbackObjects(session, sandboxRoot);

        var changes = new List<ChangeSetEntry>();
        var validation = new List<ValidationOutcome>();
        CompareObject(session.Root, currentDocument, desiredDocument, "$", changes, validation);
        ValidateInsertedSubtrees(sandboxRoot, changes, validation);
        ValidateSceneFrameSize(sandboxRoot, validation);
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

    private static void AddRelativeKeyFrameRangeWarnings(
        JsonObject desiredDocument,
        List<ValidationOutcome> validation)
    {
        if (desiredDocument.TryGetPropertyValue("Elements", out JsonNode? elementsNode)
            && elementsNode is JsonArray elements)
        {
            AddRelativeKeyFrameRangeWarningsForElements(elements, "$/Elements", validation);
        }
    }

    private static void AddRelativeKeyFrameRangeWarningsForElements(
        JsonArray elements,
        string path,
        List<ValidationOutcome> validation)
    {
        for (int i = 0; i < elements.Count; i++)
        {
            if (elements[i] is not JsonObject element)
            {
                continue;
            }

            string elementPath = CreateArrayItemPath(path, i, element);
            TimeSpan? elementLength = ReadTimeSpan(element, nameof(Element.Length))
                                      ?? ReadTimeSpan(element, nameof(EngineObject.Duration));
            if (elementLength is null)
            {
                continue;
            }

            string elementStart = ReadTimeSpan(element, nameof(Element.Start))?.ToString("c") ?? TimeSpan.Zero.ToString("c");
            string elementName = ReadString(element, nameof(CoreObject.Name)) ?? "(unnamed)";
            if (element.TryGetPropertyValue(nameof(Element.Objects), out JsonNode? objectsNode)
                && objectsNode is JsonArray objects)
            {
                AddRelativeKeyFrameRangeWarningsInNode(
                    objects,
                    elementLength.Value,
                    elementStart,
                    elementName,
                    $"{elementPath}/Objects",
                    validation);
            }
        }
    }

    private static void AddRelativeKeyFrameRangeWarningsInNode(
        JsonNode? node,
        TimeSpan elementLength,
        string elementStart,
        string elementName,
        string path,
        List<ValidationOutcome> validation)
    {
        if (node is JsonObject obj)
        {
            AddRelativeKeyFrameRangeWarningsForAnimation(
                obj,
                elementLength,
                elementStart,
                elementName,
                path,
                validation);

            foreach (KeyValuePair<string, JsonNode?> pair in obj)
            {
                AddRelativeKeyFrameRangeWarningsInNode(
                    pair.Value,
                    elementLength,
                    elementStart,
                    elementName,
                    $"{path}/{pair.Key}",
                    validation);
            }
        }
        else if (node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                JsonNode? item = array[i];
                string itemPath = item is JsonObject itemObject
                    ? CreateArrayItemPath(path, i, itemObject)
                    : $"{path}[{i}]";
                AddRelativeKeyFrameRangeWarningsInNode(
                    item,
                    elementLength,
                    elementStart,
                    elementName,
                    itemPath,
                    validation);
            }
        }
    }

    private static void AddRelativeKeyFrameRangeWarningsForAnimation(
        JsonObject animation,
        TimeSpan elementLength,
        string elementStart,
        string elementName,
        string path,
        List<ValidationOutcome> validation)
    {
        if (!animation.TryGetPropertyValue(nameof(KeyFrameAnimation.KeyFrames), out JsonNode? keyFramesNode)
            || keyFramesNode is not JsonArray keyFrames
            || ReadBool(animation, nameof(KeyFrameAnimation.UseGlobalClock)) == true)
        {
            return;
        }

        for (int i = 0; i < keyFrames.Count; i++)
        {
            if (keyFrames[i] is not JsonObject keyFrame
                || ReadTimeSpan(keyFrame, nameof(KeyFrame.KeyTime)) is not { } keyTime
                || (keyTime >= TimeSpan.Zero && keyTime <= elementLength))
            {
                continue;
            }

            string keyFramePath = CreateArrayItemPath($"{path}/KeyFrames", i, keyFrame);
            string lengthText = elementLength.ToString("c");
            string message = $"UseGlobalClock=false keyframe at '{keyFramePath}' has KeyTime '{keyTime:c}' outside Element '{elementName}' local range '00:00:00'..'{lengthText}' (Element Start '{elementStart}', Length '{lengthText}').";
            string hint = "For UseGlobalClock=false, KeyTime is local to the owning timeline element. Use 00:00:00..Element.Length, or set UseGlobalClock=true when the KeyTime values are scene timeline times.";
            validation.Add(ValidationOutcome.Warning(keyTime.ToString("c"), message, hint));
        }
    }

    private static string CreateArrayItemPath(string path, int index, JsonObject obj)
    {
        return CollectionReconciler.TryGetId(obj, out Guid id)
            ? $"{path}[Id={id}]"
            : $"{path}[{index}]";
    }

    private static string? ReadString(JsonObject obj, string propertyName)
    {
        return obj.TryGetPropertyValue(propertyName, out JsonNode? node)
               && node?.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : null;
    }

    private static bool? ReadBool(JsonObject obj, string propertyName)
    {
        if (!obj.TryGetPropertyValue(propertyName, out JsonNode? node) || node is null)
        {
            return null;
        }

        return node.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(node.GetValue<string>(), out bool value) => value,
            _ => null
        };
    }

    private static TimeSpan? ReadTimeSpan(JsonObject obj, string propertyName)
    {
        if (!obj.TryGetPropertyValue(propertyName, out JsonNode? node) || node is null)
        {
            return null;
        }

        return node.GetValueKind() == JsonValueKind.String
               && TimeSpan.TryParse(node.GetValue<string>(), out TimeSpan value)
            ? value
            : null;
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
        session.History.ExecuteInTransaction(
            () =>
            {
                session.Documents.Write(session.Root, desiredDocument);
                if (session.Root is Scene scene)
                {
                    ProjectOperations.EnsureElementUrisWithinProject(scene);
                }
            },
            "Agent edit");

        if (session is FileEditingSession fileSession)
        {
            fileSession.MarkDirty();
        }

        return new ReconcileResult(plan, session.Documents.Read(session.Root));
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

    // Applies the full desired document to a throwaway clone of the current root, so newly inserted
    // subtrees exist and are findable by Id here — unlike the live root the diff runs against.
    private static CoreObject BuildValidationSandbox(
        IEditingSession session,
        JsonObject currentDocument,
        JsonObject desiredDocument)
    {
        CoreObject sandboxRoot = CloneCurrentRoot(session, currentDocument);
        JsonObject payload = (JsonObject)desiredDocument.DeepClone();
        payload.Remove(SchemaVersion.PropertyName);

        try
        {
            new DeclarativeDocumentApplier().Apply(sandboxRoot, payload);
        }
        catch (ReconcileException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Desired document could not be applied in the validation sandbox: {ex.Message}",
                null,
                "Call get_schema for the concrete type, then retry apply_edit with the serialized property shapes returned by the schema."));
        }

        return sandboxRoot;
    }

    // Inserted subtrees never reach CompareObject (CompareArray records the InsertChild and continues),
    // so their properties are otherwise unvalidated. Re-run per-property validation against the sandbox,
    // where the insert has been applied and is findable by Id, so a new Element/Object with an invalid
    // Start/Length (or any validator-constrained property) is rejected instead of saved.
    private static void ValidateInsertedSubtrees(
        CoreObject sandboxRoot,
        List<ChangeSetEntry> changes,
        List<ValidationOutcome> validation)
    {
        foreach (ChangeSetEntry change in changes)
        {
            if (change.Operation == ChangeOperations.InsertChild && change.NewValue is JsonObject inserted)
            {
                ValidateInsertedNode(sandboxRoot, inserted, validation);
            }
        }
    }

    private static void ValidateInsertedNode(
        CoreObject sandboxRoot,
        JsonObject node,
        List<ValidationOutcome> validation)
    {
        // Element.Start/Length carry no property validator (the add/move tools reject bad values with an
        // explicit check), so validate the applied sandbox element directly; otherwise a patch could
        // insert an element with a negative Start or non-positive Length.
        if (CollectionReconciler.TryGetId(node, out Guid nodeId)
            && IdentityHelper.FindById(sandboxRoot, nodeId) is Element element)
        {
            ValidateInsertedElementTimeline(element, validation);
        }

        foreach (KeyValuePair<string, JsonNode?> pair in node)
        {
            if (ShouldSkip(pair.Key))
            {
                continue;
            }

            switch (pair.Value)
            {
                case JsonObject childObject:
                    ValidateInsertedNode(sandboxRoot, childObject, validation);
                    break;
                case JsonArray childArray:
                    foreach (JsonObject childItem in childArray.OfType<JsonObject>())
                    {
                        ValidateInsertedNode(sandboxRoot, childItem, validation);
                    }

                    break;
                default:
                    AddValidation(sandboxRoot, node, pair.Key, pair.Value, validation);
                    break;
            }
        }
    }

    private static void ValidateInsertedElementTimeline(Element element, List<ValidationOutcome> validation)
    {
        string identity = string.IsNullOrWhiteSpace(element.Name)
            ? element.Id.ToString()
            : $"'{element.Name}' ({element.Id})";
        if (element.Start < TimeSpan.Zero)
        {
            validation.Add(ValidationOutcome.Rejected(
                element.Start.ToString("c"),
                $"Element {identity} Start '{element.Start:c}' must be non-negative.",
                $"Set a non-negative Start on element {element.Id}."));
        }

        if (element.Length <= TimeSpan.Zero)
        {
            validation.Add(ValidationOutcome.Rejected(
                element.Length.ToString("c"),
                $"Element {identity} Length '{element.Length:c}' must be positive.",
                $"Set a positive Length on element {element.Id}."));
        }
    }

    // A full desired document or merge patch can set Scene Width/Height to a non-positive value that
    // create_project/add_scene would reject on their own inputs but no per-property validator covers
    // here, so an impossible canvas size would otherwise reach render/export.
    private static void ValidateSceneFrameSize(CoreObject sandboxRoot, List<ValidationOutcome> validation)
    {
        if (sandboxRoot is Scene scene && (scene.FrameSize.Width <= 0 || scene.FrameSize.Height <= 0))
        {
            validation.Add(ValidationOutcome.Rejected(
                $"{scene.FrameSize.Width}x{scene.FrameSize.Height}",
                $"Scene frame size '{scene.FrameSize.Width}x{scene.FrameSize.Height}' must be positive.",
                "Set Width and Height to positive pixel values before applying."));
        }
    }

    private static void ValidateNoNewFallbackObjects(IEditingSession session, CoreObject sandboxRoot)
    {
        HashSet<Guid> existingFallbackIds = CollectFallbackIds(session.Root);
        if (FindFirstNewFallback(sandboxRoot, "$", existingFallbackIds) is { } occurrence)
        {
            string typeDetail = string.IsNullOrWhiteSpace(occurrence.FallbackTypeName)
                ? "unknown serialized type"
                : occurrence.FallbackTypeName;
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Desired document produced a fallback object at '{occurrence.Path}' for {typeDetail}.",
                occurrence.Path,
                CreateFallbackHint(occurrence)));
        }
    }

    private static CoreObject CloneCurrentRoot(IEditingSession session, JsonObject currentDocument)
    {
        JsonObject snapshot = (JsonObject)currentDocument.DeepClone();
        snapshot.Remove(SchemaVersion.PropertyName);
        if (session.Root.Uri is { } rootUri)
        {
            snapshot["Uri"] = rootUri.ToString();
        }

        var clone = (CoreObject)CoreSerializer.DeserializeFromJsonObject(
            snapshot,
            session.Root.GetType(),
            new CoreSerializerOptions
            {
                BaseUri = session.Root.Uri,
                Mode = CoreSerializationMode.Read | CoreSerializationMode.EmbedReferencedObjects
            });
        clone.Uri ??= session.Root.Uri;
        return clone;
    }

    private static HashSet<Guid> CollectFallbackIds(CoreObject root)
    {
        var ids = new HashSet<Guid>();
        if (root is IHierarchical hierarchical)
        {
            foreach (IFallback fallback in hierarchical.EnumerateAllChildren<IFallback>())
            {
                if (fallback is CoreObject coreObject)
                {
                    ids.Add(coreObject.Id);
                }
            }
        }

        if (root is IFallback rootFallback)
        {
            ids.Add(((CoreObject)rootFallback).Id);
        }

        return ids;
    }

    private static FallbackOccurrence? FindFirstNewFallback(
        CoreObject root,
        string path,
        HashSet<Guid> existingFallbackIds)
    {
        var visited = new HashSet<Guid>();
        return FindFirstNewFallbackCore(root, path, existingFallbackIds, visited);
    }

    private static FallbackOccurrence? FindFirstNewFallbackCore(
        CoreObject node,
        string path,
        HashSet<Guid> existingFallbackIds,
        HashSet<Guid> visited)
    {
        if (!visited.Add(node.Id))
        {
            return null;
        }

        if (node is IFallback fallback && !existingFallbackIds.Contains(node.Id))
        {
            fallback.TryGetTypeName(out string? fallbackTypeName);
            return new FallbackOccurrence(
                path,
                node.Id,
                fallbackTypeName,
                fallback.Reason.ToString(),
                fallback.ErrorMessage);
        }

        switch (node)
        {
            case Scene scene:
                for (int i = 0; i < scene.Children.Count; i++)
                {
                    if (FindFirstNewFallbackCore(
                            scene.Children[i],
                            $"{path}/Elements[{i}]",
                            existingFallbackIds,
                            visited) is { } occurrence)
                    {
                        return occurrence;
                    }
                }
                break;

            case Element element:
                for (int i = 0; i < element.Objects.Count; i++)
                {
                    if (FindFirstNewFallbackCore(
                            element.Objects[i],
                            $"{path}/Objects[{i}]",
                            existingFallbackIds,
                            visited) is { } occurrence)
                    {
                        return occurrence;
                    }
                }
                break;

            case EngineObject engineObject:
                foreach (IProperty property in engineObject.Properties)
                {
                    if (FindFirstNewFallbackInValue(
                            property.CurrentValue,
                            $"{path}/{property.Name}",
                            existingFallbackIds,
                            visited) is { } occurrence)
                    {
                        return occurrence;
                    }
                }
                break;
        }

        return null;
    }

    private static FallbackOccurrence? FindFirstNewFallbackInValue(
        object? value,
        string path,
        HashSet<Guid> existingFallbackIds,
        HashSet<Guid> visited)
    {
        switch (value)
        {
            case CoreObject coreObject:
                return FindFirstNewFallbackCore(coreObject, path, existingFallbackIds, visited);
            case IEnumerable enumerable when value is not string:
                {
                    int index = 0;
                    foreach (object? item in enumerable)
                    {
                        if (FindFirstNewFallbackInValue(
                                item,
                                $"{path}[{index}]",
                                existingFallbackIds,
                                visited) is { } occurrence)
                        {
                            return occurrence;
                        }

                        index++;
                    }

                    break;
                }
        }

        return null;
    }

    private static string CreateFallbackHint(FallbackOccurrence occurrence)
    {
        string baseHint = "Call get_schema for the exact drawable/effect/brush/transform/pen/animation type and use the discriminator and PascalCase property names it returns. Timeline Elements use '$type': '[Beutl.ProjectSystem]:Element'. Objects require concrete EngineObject discriminators from get_schema; typed property values such as Pen, Brush, Transform, Effect, and Animation also require concrete schema-returned object shapes.";
        if (!string.IsNullOrWhiteSpace(occurrence.Message))
        {
            return $"{baseHint} Deserialization error: {occurrence.Message}";
        }

        return $"{baseHint} Fallback reason: {occurrence.Reason}.";
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

        return document;
    }

    private static void CompareObject(
        CoreObject root,
        JsonObject current,
        JsonObject desired,
        string path,
        List<ChangeSetEntry> changes,
        List<ValidationOutcome> validation)
    {
        string? targetId = desired.TryGetPropertyValue(nameof(CoreObject.Id), out JsonNode? idNode)
            ? idNode?.GetValue<string>()
            : null;

        foreach (KeyValuePair<string, JsonNode?> pair in desired)
        {
            if (ShouldSkip(pair.Key))
            {
                continue;
            }

            current.TryGetPropertyValue(pair.Key, out JsonNode? currentNode);
            string childPath = $"{path}/{pair.Key}";

            if (currentNode is JsonObject currentObject && pair.Value is JsonObject desiredObject)
            {
                CompareObject(root, currentObject, desiredObject, childPath, changes, validation);
            }
            else if (currentNode is JsonArray currentArray && pair.Value is JsonArray desiredArray)
            {
                CompareArray(root, currentArray, desiredArray, childPath, changes, validation);
            }
            else if (!JsonEquals(currentNode, pair.Value))
            {
                changes.Add(new ChangeSetEntry(
                    ChangeOperations.SetProperty,
                    childPath,
                    targetId,
                    currentNode?.DeepClone(),
                    pair.Value?.DeepClone()));
                AddValidation(root, desired, pair.Key, pair.Value, validation);
            }
        }

        foreach (KeyValuePair<string, JsonNode?> pair in current)
        {
            if (!ShouldSkip(pair.Key) && !desired.ContainsKey(pair.Key))
            {
                changes.Add(new ChangeSetEntry(
                    ChangeOperations.SetProperty,
                    $"{path}/{pair.Key}",
                    targetId,
                    pair.Value?.DeepClone(),
                    null));
            }
        }
    }

    private static void CompareArray(
        CoreObject root,
        JsonArray current,
        JsonArray desired,
        string path,
        List<ChangeSetEntry> changes,
        List<ValidationOutcome> validation)
    {
        if (!CollectionReconciler.IsIdentityArray(current) && !CollectionReconciler.IsIdentityArray(desired))
        {
            if (!JsonEquals(current, desired))
            {
                changes.Add(new ChangeSetEntry(
                    ChangeOperations.SetProperty,
                    path,
                    null,
                    current.DeepClone(),
                    desired.DeepClone()));
            }

            return;
        }

        Dictionary<Guid, (int Index, JsonObject Node)> currentById = IndexById(current);
        Dictionary<Guid, (int Index, JsonObject Node)> desiredById = IndexById(desired);

        foreach (KeyValuePair<Guid, (int Index, JsonObject Node)> desiredItem in desiredById)
        {
            if (!currentById.TryGetValue(desiredItem.Key, out (int Index, JsonObject Node) currentItem))
            {
                changes.Add(new ChangeSetEntry(
                    ChangeOperations.InsertChild,
                    path,
                    desiredItem.Key.ToString(),
                    null,
                    desiredItem.Value.Node.DeepClone(),
                    desiredItem.Value.Index));
                continue;
            }

            if (currentItem.Index != desiredItem.Value.Index)
            {
                changes.Add(new ChangeSetEntry(
                    ChangeOperations.MoveChild,
                    path,
                    desiredItem.Key.ToString(),
                    null,
                    null,
                    desiredItem.Value.Index));
            }

            CompareObject(root, currentItem.Node, desiredItem.Value.Node, $"{path}[Id={desiredItem.Key}]", changes, validation);
        }

        foreach (KeyValuePair<Guid, (int Index, JsonObject Node)> currentItem in currentById)
        {
            if (!desiredById.ContainsKey(currentItem.Key))
            {
                changes.Add(new ChangeSetEntry(
                    ChangeOperations.RemoveChild,
                    path,
                    currentItem.Key.ToString(),
                    currentItem.Value.Node.DeepClone(),
                    null,
                    currentItem.Value.Index));
            }
        }
    }

    private static Dictionary<Guid, (int Index, JsonObject Node)> IndexById(JsonArray array)
    {
        var result = new Dictionary<Guid, (int Index, JsonObject Node)>();
        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is JsonObject obj && CollectionReconciler.TryGetId(obj, out Guid id))
            {
                result[id] = (i, obj);
            }
        }

        return result;
    }

    private static void AddValidation(
        CoreObject root,
        JsonObject desiredOwner,
        string propertyName,
        JsonNode? valueNode,
        List<ValidationOutcome> validation)
    {
        if (!CollectionReconciler.TryGetId(desiredOwner, out Guid ownerId))
        {
            return;
        }

        if (IdentityHelper.FindById(root, ownerId) is not CoreObject target)
        {
            return;
        }

        try
        {
            if (target is KeyFrame && propertyName == nameof(KeyFrame.Easing))
            {
                string? easingError = DeclarativeDocumentApplier.ValidateEasingNode(valueNode);
                validation.Add(easingError is null
                    ? ValidationOutcome.Ok(valueNode?.DeepClone())
                    : ValidationOutcome.Rejected(null, $"{propertyName}: {easingError}", null));
                return;
            }

            CoreSerializerOptions options = DeclarativeDocumentApplier.CreateOptions(target);

            if (PropertyRegistry.FindRegistered(target, propertyName) is { } coreProperty)
            {
                object? value = valueNode is null
                    ? null
                    : EnumJsonValueNormalizer.Deserialize(valueNode, coreProperty.PropertyType, options);
                validation.Add(ValidationEvaluator.Evaluate(target, coreProperty, value));
                return;
            }

            if (target is EngineObject engineObject
                && engineObject.Properties.FirstOrDefault(p => p.Name == propertyName) is { } engineProperty)
            {
                object? value = valueNode is null
                    ? null
                    : EnumJsonValueNormalizer.Deserialize(valueNode, engineProperty.ValueType, options);
                validation.Add(ValidationEvaluator.Evaluate(engineProperty, value));
            }
        }
        catch (Exception ex)
        {
            Type? targetType = ResolvePropertyType(target, propertyName);
            validation.Add(ValidationOutcome.Rejected(
                null,
                $"{propertyName}: {ex.Message}",
                targetType is null ? null : ValidationEvaluator.CreateValueHint(targetType)));
        }
    }

    private static Type? ResolvePropertyType(CoreObject target, string propertyName)
    {
        if (PropertyRegistry.FindRegistered(target, propertyName) is { } coreProperty)
        {
            return coreProperty.PropertyType;
        }

        return target is EngineObject engineObject
            ? engineObject.Properties.FirstOrDefault(p => p.Name == propertyName)?.ValueType
            : null;
    }

    private static bool ShouldSkip(string propertyName)
    {
        return propertyName is SchemaVersion.PropertyName or "$type" or nameof(CoreObject.Id) or "Uri";
    }

    private static bool JsonEquals(JsonNode? left, JsonNode? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.ToJsonString() == right.ToJsonString();
    }

    private sealed record FallbackOccurrence(
        string Path,
        Guid Id,
        string? FallbackTypeName,
        string Reason,
        string? Message);
}
