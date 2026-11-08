using System.Text.Json;
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
        // A merge-patched shorthand can retain the previous long-form KeyFrames member beside $kf.
        // Expand first so the new shorthand envelope wins, matching DeclarativeDocumentApplier.
        JsonObject normalized = KeyFrameShorthand.IsShorthand(animation)
            ? KeyFrameShorthand.Expand(animation, typeof(object))
            : animation;
        if (!normalized.TryGetPropertyValue(nameof(IKeyFrameAnimation.KeyFrames), out JsonNode? keyFramesNode)
            || keyFramesNode is not JsonArray keyFrames
            || ReadBool(normalized, nameof(IKeyFrameAnimation.UseGlobalClock)) == true)
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
            validation.Add(ValidationOutcome.Warning(keyTime.ToString("c"), message, options: null, hint));
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

    // A base field holds only a value and takes no animation.
    private static IProperty? FindAnimationTarget(EngineObject engineObject, string propertyName)
        => engineObject.Properties.FirstOrDefault(
            property => property.Name == propertyName && !engineObject.IsBaseField(property));

    private static bool ExpandAnimationShorthand(ObjectLookup sandbox, JsonNode? node)
    {
        bool expanded = false;
        if (node is JsonObject obj)
        {
            if (CollectionReconciler.TryGetId(obj, out Guid id)
                && sandbox.Find(id) is EngineObject engineObject)
            {
                expanded |= ExpandOwnAnimationShorthand(engineObject, obj);
            }

            foreach ((_, JsonNode? child) in obj.ToArray())
            {
                expanded |= ExpandAnimationShorthand(sandbox, child);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
            {
                expanded |= ExpandAnimationShorthand(sandbox, child);
            }
        }

        return expanded;
    }

    private static bool ExpandOwnAnimationShorthand(EngineObject owner, JsonObject ownerJson)
    {
        if (ownerJson["Animations"] is not JsonObject animations)
        {
            return false;
        }

        bool expanded = false;
        foreach ((string propertyName, JsonNode? animationNode) in animations.ToArray())
        {
            if (animationNode is JsonObject animationJson
                && KeyFrameShorthand.IsShorthand(animationJson)
                && FindAnimationTarget(owner, propertyName) is { } property)
            {
                animations[propertyName] = KeyFrameShorthand.Expand(animationJson, property.ValueType);
                expanded = true;
            }
        }

        return expanded;
    }

    // A new object nested in a property has no Id for the sandbox to find it by, and the serializer that reads it
    // does not know the shorthand. The document says what it is, by its $type or by the property holding it.
    private static void ExpandNewObjectAnimationShorthand(JsonObject document)
    {
        if (ContainsAnimationShorthand(document))
        {
            ExpandAnimationShorthandByType(document, null, new Dictionary<Type, EngineObject?>());
        }
    }

    private static bool ContainsAnimationShorthand(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => KeyFrameShorthand.IsShorthand(obj) || obj.Any(pair => ContainsAnimationShorthand(pair.Value)),
            JsonArray array => array.Any(ContainsAnimationShorthand),
            _ => false
        };
    }

    private static void ExpandAnimationShorthandByType(
        JsonNode? node,
        Type? declaredType,
        Dictionary<Type, EngineObject?> prototypes)
    {
        if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                ExpandAnimationShorthandByType(item, declaredType, prototypes);
            }

            return;
        }

        if (node is not JsonObject obj)
        {
            return;
        }

        EngineObject? prototype = FindPrototype(obj, declaredType, prototypes);
        if (prototype is not null
            && ExpandOwnAnimationShorthand(prototype, obj)
            && !obj.ContainsKey("$type")
            && !CollectionReconciler.TryGetId(obj, out _))
        {
            // A new object gets its Id from its $type, and the owning properties check its keyframe values through that Id.
            obj["$type"] = IdentityHelper.WriteDiscriminator(prototype.GetType());
        }

        foreach ((string name, JsonNode? child) in obj.ToArray())
        {
            IProperty? property = prototype?.Properties.FirstOrDefault(item => item.Name == name);
            ExpandAnimationShorthandByType(
                child,
                property is IListProperty list ? list.ElementType : property?.ValueType,
                prototypes);
        }
    }

    // An instance of the object's type, to name the types of its properties; null for what has none to offer.
    private static EngineObject? FindPrototype(
        JsonObject obj,
        Type? declaredType,
        Dictionary<Type, EngineObject?> prototypes)
    {
        Type? type = declaredType is null ? obj.GetDiscriminator() : CoreSerializer.ResolveActualType(obj, declaredType);
        if (type is null || type.IsAbstract || !typeof(EngineObject).IsAssignableFrom(type))
        {
            return null;
        }

        if (!prototypes.TryGetValue(type, out EngineObject? prototype))
        {
            prototype = type.GetConstructor(Type.EmptyTypes) is null ? null : (EngineObject?)Activator.CreateInstance(type);
            prototypes[type] = prototype;
        }

        return prototype;
    }

    private static void ValidateChangedAnimationValues(
        ObjectLookup sandbox,
        JsonObject currentDocument,
        JsonObject desiredDocument,
        List<ValidationOutcome> validation)
    {
        HashSet<Guid> ambiguousIds = CollectionReconciler.CollectDuplicatedIds(currentDocument);
        ValidateChangedAnimationValuesInNode(
            sandbox,
            currentDocument,
            desiredDocument,
            ambiguousIds,
            validation);
    }

    private static void ValidateChangedAnimationValuesInNode(
        ObjectLookup sandbox,
        JsonNode? currentNode,
        JsonNode? desiredNode,
        IReadOnlySet<Guid> ambiguousIds,
        List<ValidationOutcome> validation)
    {
        if (desiredNode is JsonObject desiredObject)
        {
            JsonObject? currentObject = currentNode as JsonObject;
            if (CollectionReconciler.TryGetId(desiredObject, out Guid id))
            {
                ValidateOwnerAnimations(sandbox, currentObject, desiredObject, id, ambiguousIds, validation);
            }

            foreach ((string propertyName, JsonNode? desiredChild) in desiredObject)
            {
                JsonNode? currentChild = null;
                currentObject?.TryGetPropertyValue(propertyName, out currentChild);
                ValidateChangedAnimationValuesInNode(
                    sandbox,
                    currentChild,
                    desiredChild,
                    ambiguousIds,
                    validation);
            }
        }
        else if (desiredNode is JsonArray desiredArray)
        {
            ValidateChangedAnimationValuesInArray(
                sandbox,
                currentNode as JsonArray,
                desiredArray,
                ambiguousIds,
                validation);
        }
    }

    private static void ValidateOwnerAnimations(
        ObjectLookup sandbox,
        JsonObject? currentObject,
        JsonObject desiredObject,
        Guid id,
        IReadOnlySet<Guid> ambiguousIds,
        List<ValidationOutcome> validation)
    {
        JsonObject? currentAnimations = currentObject?["Animations"] as JsonObject;
        JsonObject? desiredAnimations = desiredObject["Animations"] as JsonObject;
        if (ambiguousIds.Contains(id))
        {
            if (!JsonEquals(currentAnimations, desiredAnimations))
            {
                validation.Add(ValidationOutcome.Rejected(
                    null,
                    $"Animation changes cannot target owner Id '{id}' because it occurs more than once in the current document.",
                    options: null,
                    "Assign a unique Id to each duplicated object first, then retry the animation edit using the repaired object's unique Id."));
            }

            return;
        }

        if (desiredAnimations is null
            || sandbox.Find(id) is not EngineObject engineObject)
        {
            return;
        }

        foreach ((string propertyName, JsonNode? animationNode) in desiredAnimations)
        {
            JsonNode? currentAnimationNode = null;
            currentAnimations?.TryGetPropertyValue(propertyName, out currentAnimationNode);
            if (JsonEquals(currentAnimationNode, animationNode)
                || animationNode is not JsonObject animationJson
                || FindAnimationTarget(engineObject, propertyName) is not { } property)
            {
                continue;
            }

            ValidateAnimationKeyFrameValues(
                sandbox,
                engineObject,
                property,
                currentAnimationNode,
                animationJson,
                validation);
        }
    }

    private static void ValidateAnimationKeyFrameValues(
        ObjectLookup sandbox,
        EngineObject engineObject,
        IProperty property,
        JsonNode? currentAnimationNode,
        JsonObject animationJson,
        List<ValidationOutcome> validation)
    {
        JsonObject normalizedAnimation = KeyFrameShorthand.IsShorthand(animationJson)
            ? KeyFrameShorthand.Expand(animationJson, property.ValueType)
            : animationJson;
        if (normalizedAnimation[nameof(IKeyFrameAnimation.KeyFrames)] is not JsonArray keyFrames)
        {
            return;
        }

        HashSet<Guid>? changedValueIds = KeyFrameValueChangeDetector.CollectChangedValueIds(
            currentAnimationNode as JsonObject,
            normalizedAnimation);
        CoreSerializerOptions options = DeclarativeDocumentApplier.CreateOptions(
            DeclarativeDocumentApplier.ResolveBaseUri(engineObject) ?? sandbox.Root.Uri);
        foreach (JsonObject keyFrame in keyFrames.OfType<JsonObject>())
        {
            if (changedValueIds is not null
                && CollectionReconciler.TryGetId(keyFrame, out Guid keyFrameId)
                && !changedValueIds.Contains(keyFrameId))
            {
                continue;
            }

            if (!keyFrame.TryGetPropertyValue(nameof(KeyFrame<float>.Value), out JsonNode? valueNode))
            {
                continue;
            }

            ValidateKeyFrameValue(property, keyFrame, valueNode, options, validation);
        }
    }

    private static void ValidateKeyFrameValue(
        IProperty property,
        JsonObject keyFrame,
        JsonNode? valueNode,
        CoreSerializerOptions options,
        List<ValidationOutcome> validation)
    {
        try
        {
            object? value = valueNode is null
                ? null
                : EnumJsonValueNormalizer.Deserialize(valueNode, property.ValueType, options);
            ValidationOutcome outcome = ValidationEvaluator.Evaluate(
                property,
                value,
                options);
            validation.Add(outcome);
            if (outcome.Status == ValidationStatus.Coerced)
            {
                // CompareObject and the live applier must consume the same accepted
                // value. Otherwise the response reports the caller's pre-coercion input
                // while the owning property silently installs the coerced value.
                keyFrame[nameof(KeyFrame<float>.Value)] = outcome.CoercedValue?.DeepClone();
            }
        }
        catch (Exception ex)
        {
            validation.Add(ValidationOutcome.Rejected(
                valueNode,
                $"Animation value for property '{property.Name}' is invalid: {ex.Message}",
                options,
                ValidationEvaluator.CreateValueHint(property.ValueType)));
        }
    }

    private static void ValidateChangedAnimationValuesInArray(
        ObjectLookup sandbox,
        JsonArray? currentArray,
        JsonArray desiredArray,
        IReadOnlySet<Guid> ambiguousIds,
        List<ValidationOutcome> validation)
    {
        if (currentArray is not null
            && (CollectionReconciler.IsIdentityArray(currentArray)
                || CollectionReconciler.IsIdentityArray(desiredArray)))
        {
            Dictionary<Guid, List<JsonObject>> currentById = GroupOccurrencesById(currentArray);
            foreach (JsonNode? desiredChild in desiredArray)
            {
                JsonNode? currentChild = null;
                if (desiredChild is JsonObject desiredItem
                    && CollectionReconciler.TryGetId(desiredItem, out Guid desiredId)
                    && currentById.TryGetValue(desiredId, out List<JsonObject>? occurrences))
                {
                    currentChild = TakeMatchingOccurrence(occurrences, desiredItem);
                }

                ValidateChangedAnimationValuesInNode(
                    sandbox,
                    currentChild,
                    desiredChild,
                    ambiguousIds,
                    validation);
            }
        }
        else
        {
            for (int i = 0; i < desiredArray.Count; i++)
            {
                ValidateChangedAnimationValuesInNode(
                    sandbox,
                    currentArray is not null && i < currentArray.Count ? currentArray[i] : null,
                    desiredArray[i],
                    ambiguousIds,
                    validation);
            }
        }
    }

    private static Dictionary<Guid, List<JsonObject>> GroupOccurrencesById(JsonArray currentArray)
    {
        var currentById = new Dictionary<Guid, List<JsonObject>>();
        foreach (JsonObject currentObject in currentArray.OfType<JsonObject>())
        {
            if (CollectionReconciler.TryGetId(currentObject, out Guid id))
            {
                if (!currentById.TryGetValue(id, out List<JsonObject>? occurrences))
                {
                    occurrences = [];
                    currentById.Add(id, occurrences);
                }

                occurrences.Add(currentObject);
            }
        }

        return currentById;
    }

    private static JsonObject? TakeMatchingOccurrence(
        List<JsonObject> currentOccurrences,
        JsonObject desiredOccurrence)
    {
        if (currentOccurrences.Count == 0)
        {
            return null;
        }

        int index = currentOccurrences.FindIndex(candidate => JsonEquals(candidate, desiredOccurrence));
        if (index < 0)
        {
            index = currentOccurrences.FindIndex(candidate =>
                JsonEqualsExceptAnimations(candidate, desiredOccurrence));
        }

        if (index < 0)
        {
            JsonObject? desiredAnimations = desiredOccurrence["Animations"] as JsonObject;
            index = currentOccurrences.FindIndex(candidate =>
                JsonEquals(candidate["Animations"] as JsonObject, desiredAnimations));
        }

        if (index < 0)
        {
            index = 0;
        }

        JsonObject result = currentOccurrences[index];
        currentOccurrences.RemoveAt(index);
        return result;
    }

    private static bool JsonEqualsExceptAnimations(JsonObject left, JsonObject right)
    {
        int leftCount = 0;
        foreach ((string propertyName, JsonNode? leftValue) in left)
        {
            if (propertyName == "Animations")
            {
                continue;
            }

            leftCount++;
            if (!right.TryGetPropertyValue(propertyName, out JsonNode? rightValue)
                || !JsonEquals(leftValue, rightValue))
            {
                return false;
            }
        }

        int rightCount = right.Count(pair => pair.Key != "Animations");
        return leftCount == rightCount;
    }
}
