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
    public ReconcilePlan Plan(IEditingSession session, JsonObject desired)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(desired);

        JsonObject desiredDocument = PrepareDesired(session, desired);
        return PlanPrepared(session, desiredDocument);
    }

    private static ReconcilePlan PlanPrepared(IEditingSession session, JsonObject desiredDocument)
    {
        JsonObject currentDocument = session.Documents.Read(session.Root);
        HashSet<Guid> newIds = CollectionReconciler.MintMissingIds(desiredDocument);
        if (CollectionReconciler.ValidateIdentityReferences(currentDocument, desiredDocument, newIds) is { } error)
        {
            throw new ReconcileException(error);
        }

        var changes = new List<ChangeSetEntry>();
        var validation = new List<ValidationOutcome>();
        CompareObject(session.Root, currentDocument, desiredDocument, "$", changes, validation);

        if (validation.FirstOrDefault(item => item.Status == ValidationStatus.Rejected) is { } rejected)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                rejected.Message ?? "Validation rejected the requested value."));
        }

        return new ReconcilePlan(changes, validation);
    }

    public ReconcileResult Apply(IEditingSession session, JsonObject desired)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(desired);

        JsonObject desiredDocument = PrepareDesired(session, desired);
        ReconcilePlan plan = PlanPrepared(session, desiredDocument);
        void Mutate()
        {
            session.History.ExecuteInTransaction(
                () =>
                {
                    session.Documents.Write(session.Root, desiredDocument);
                    if (session.Root is Scene scene)
                    {
                        ProjectOperations.EnsureElementUris(scene);
                    }
                },
                "Agent edit");
        }

        if (session is IEditingSessionDispatcher dispatcher)
        {
            dispatcher.Invoke(Mutate);
        }
        else
        {
            Mutate();
        }

        if (session is FileEditingSession fileSession)
        {
            fileSession.MarkDirty();
        }

        return new ReconcileResult(plan, session.Documents.Read(session.Root));
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
                validation.Add(ValidationOutcome.Ok(valueNode?.DeepClone()));
                return;
            }

            if (PropertyRegistry.FindRegistered(target, propertyName) is { } coreProperty)
            {
                object? value = valueNode is null
                    ? null
                    : CoreSerializer.DeserializeFromJsonNode(valueNode.DeepClone(), coreProperty.PropertyType);
                validation.Add(ValidationEvaluator.Evaluate(target, coreProperty, value));
                return;
            }

            if (target is EngineObject engineObject
                && engineObject.Properties.FirstOrDefault(p => p.Name == propertyName) is { } engineProperty)
            {
                object? value = valueNode is null
                    ? null
                    : CoreSerializer.DeserializeFromJsonNode(valueNode.DeepClone(), engineProperty.ValueType);
                validation.Add(ValidationEvaluator.Evaluate(engineProperty, value));
            }
        }
        catch (Exception ex)
        {
            validation.Add(ValidationOutcome.Rejected(null, $"{propertyName}: {ex.Message}"));
        }
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
}
