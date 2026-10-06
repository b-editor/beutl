using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Documents;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Reconciliation;

public sealed partial class Reconciler
{
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
            ValidateElementTimeline(element, validation);
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

    // Element.Start/Length carry no property validator, so a patch that edits an existing element's
    // timing (not just an insert) can persist a negative Start or non-positive Length that the
    // add/move tools reject. Validate every element whose timing changed against the applied sandbox.
    private static void ValidateChangedElementTimelines(
        CoreObject sandboxRoot,
        List<ChangeSetEntry> changes,
        List<ValidationOutcome> validation)
    {
        var seen = new HashSet<Guid>();
        foreach (ChangeSetEntry change in changes)
        {
            if (change.Operation == ChangeOperations.SetProperty
                && change.TargetId is { } id
                && (change.Path.EndsWith("/Start", StringComparison.Ordinal)
                    || change.Path.EndsWith("/Length", StringComparison.Ordinal))
                && Guid.TryParse(id, out Guid guid)
                && seen.Add(guid)
                && IdentityHelper.FindById(sandboxRoot, guid) is Element element)
            {
                ValidateElementTimeline(element, validation);
            }
        }
    }

    private static void ValidateElementTimeline(Element element, List<ValidationOutcome> validation)
    {
        string identity = string.IsNullOrWhiteSpace(element.Name)
            ? element.Id.ToString()
            : $"'{element.Name}' ({element.Id})";
        if (element.Start < TimeSpan.Zero)
        {
            validation.Add(ValidationOutcome.Rejected(
                element.Start.ToString("c"),
                $"Element {identity} Start '{element.Start:c}' must be non-negative.",
                options: null,
                $"Set a non-negative Start on element {element.Id}."));
        }

        if (element.Length <= TimeSpan.Zero)
        {
            validation.Add(ValidationOutcome.Rejected(
                element.Length.ToString("c"),
                $"Element {identity} Length '{element.Length:c}' must be positive.",
                options: null,
                $"Set a positive Length on element {element.Id}."));
        }
    }

    // A full desired document or merge patch can set Scene Width/Height/Duration to a non-positive
    // value that create_project/add_scene reject on their own inputs but no per-property validator
    // covers here, so an impossible canvas size or zero-length scene would reach render/export.
    private static void ValidateSceneInvariants(CoreObject sandboxRoot, List<ValidationOutcome> validation)
    {
        if (sandboxRoot is not Scene scene)
        {
            return;
        }

        if (scene.FrameSize.Width <= 0 || scene.FrameSize.Height <= 0)
        {
            validation.Add(ValidationOutcome.Rejected(
                $"{scene.FrameSize.Width}x{scene.FrameSize.Height}",
                $"Scene frame size '{scene.FrameSize.Width}x{scene.FrameSize.Height}' must be positive.",
                options: null,
                "Set Width and Height to positive pixel values before applying."));
        }

        if (scene.Duration <= TimeSpan.Zero)
        {
            validation.Add(ValidationOutcome.Rejected(
                scene.Duration.ToString("c"),
                $"Scene duration '{scene.Duration:c}' must be positive.",
                options: null,
                "Set a positive Duration before applying."));
        }
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
                    ? ValidationOutcome.Ok(valueNode, options: null)
                    : ValidationOutcome.Rejected(null, $"{propertyName}: {easingError}", options: null));
                return;
            }

            CoreSerializerOptions options = DeclarativeDocumentApplier.CreateOptions(
                DeclarativeDocumentApplier.ResolveBaseUri(target) ?? root.Uri);

            if (PropertyRegistry.FindRegistered(target, propertyName) is { } coreProperty)
            {
                object? value = valueNode is null
                    ? null
                    : EnumJsonValueNormalizer.Deserialize(valueNode, coreProperty.PropertyType, options);
                validation.Add(ValidationEvaluator.Evaluate(target, coreProperty, value, options));
                return;
            }

            if (target is EngineObject engineObject
                && engineObject.Properties.FirstOrDefault(p => p.Name == propertyName) is { } engineProperty)
            {
                object? value = valueNode is null
                    ? null
                    : EnumJsonValueNormalizer.Deserialize(valueNode, engineProperty.ValueType, options);
                validation.Add(ValidationEvaluator.Evaluate(engineProperty, value, options));
            }
        }
        catch (Exception ex)
        {
            Type? targetType = ResolvePropertyType(target, propertyName);
            validation.Add(ValidationOutcome.Rejected(
                null,
                $"{propertyName}: {ex.Message}",
                options: null,
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
}
