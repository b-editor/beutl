using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Documents;

internal sealed partial class DeclarativeDocumentApplier
{
    private void ApplyKeyFrameAnimation(KeyFrameAnimation animation, JsonObject desired)
    {
        JsonObject payload = (JsonObject)desired.DeepClone();
        payload.Remove(nameof(KeyFrameAnimation.KeyFrames));
        NormalizeRegisteredPropertyValues(animation, payload);

        CoreSerializer.PopulateFromJsonObject(animation, animation.GetType(), payload, CreateOptions(animation));
        ClearAbsentRegisteredObjectProperties(animation, desired, payload);

        if (desired.TryGetPropertyValue(nameof(KeyFrameAnimation.KeyFrames), out JsonNode? keyframesNode))
        {
            ApplyKeyFrameList(animation.KeyFrames, RequireArrayMember(keyframesNode, nameof(KeyFrameAnimation.KeyFrames)), animation);
        }
        else
        {
            animation.KeyFrames.Clear();
        }
    }

    private void ApplyKeyFrame(IKeyFrame keyFrame, JsonObject desired)
    {
        if (keyFrame is CoreObject coreObject)
        {
            ApplyRegisteredProperties(
                coreObject,
                desired,
                new HashSet<string> { nameof(KeyFrame.KeyTime), nameof(KeyFrame.Easing), nameof(KeyFrame<float>.Value) });
        }

        if (desired.TryGetPropertyValue(nameof(KeyFrame<float>.Value), out JsonNode? valueNode)
            && keyFrame is CoreObject keyFrameObject
            && PropertyRegistry.FindRegistered(keyFrameObject, nameof(KeyFrame<float>.Value)) is { } valueProperty)
        {
            keyFrame.Value = valueNode is null
                ? null
                : EnumJsonValueNormalizer.Deserialize(valueNode, valueProperty.PropertyType, CreateOptions(keyFrameObject));
        }

        if (desired.TryGetPropertyValue(nameof(KeyFrame.Easing), out JsonNode? easingNode) && easingNode is not null)
        {
            JsonObject current = CoreSerializer.SerializeToJsonObject(keyFrame, CreateOptions(keyFrame as CoreObject));
            // Recovered easings retain their original JSON even while evaluating as Linear.
            // A different serialized value is an explicit repair, including a new Linear easing.
            if (!JsonNode.DeepEquals(current[nameof(KeyFrame.Easing)], easingNode))
            {
                keyFrame.Easing = DeserializeEasing(easingNode);
            }
        }

        if (desired.TryGetPropertyValue(nameof(KeyFrame.KeyTime), out JsonNode? keyTimeNode) && keyTimeNode is not null)
        {
            keyFrame.KeyTime = (TimeSpan)CoreSerializer.DeserializeFromJsonNode(
                keyTimeNode.DeepClone(),
                typeof(TimeSpan),
                CreateOptions(keyFrame as CoreObject))!;
        }
    }

    private void ApplyAnimations(EngineObject target, JsonObject desired)
    {
        JsonObject? animations = desired.TryGetPropertyValue("Animations", out JsonNode? animationsNode)
            ? RequireObjectMember(animationsNode, "Animations")
            : null;

        if (animations is not null)
        {
            RequireTargetProperties(target, animations, "Animation", static property => property.IsAnimatable, "is not animatable");
        }

        foreach (IProperty property in target.Properties.Where(property => property.IsAnimatable))
        {
            if (animations?.TryGetPropertyValue(property.Name, out JsonNode? node) == true && node is JsonObject animationJson)
            {
                ApplyAnimation(property, animationJson);
            }
            else
            {
                property.Animation = null;
            }
        }
    }

    private void ApplyAnimation(IProperty property, JsonObject animationJson)
    {
        if (KeyFrameShorthand.IsShorthand(animationJson))
        {
            animationJson = KeyFrameShorthand.Expand(animationJson, property.ValueType);
        }

        HashSet<Guid>? changedValueIds = null;
        IAnimation? current = property.Animation;
        if (current is CoreObject currentObject
            && IdentityMatches(currentObject, animationJson)
            && TypeMatches(currentObject, animationJson))
        {
            // A merge-patch produces a full desired document, so animations unrelated to the edit
            // arrive here structurally equivalent to their current serialization. Do not replay
            // their setters: attaching a validator does not retroactively change old keyframes, and
            // reassigning those values here would silently coerce or reject pre-existing project data
            // that the caller did not edit.
            JsonObject currentJson = CoreSerializer.SerializeToJsonObject(
                currentObject,
                new CoreSerializerOptions
                {
                    BaseUri = ResolveBaseUri(currentObject) ?? _documentBaseUri,
                    Mode = CoreSerializationMode.EmbedReferencedObjects
                });
            if (JsonNode.DeepEquals(currentJson, animationJson))
            {
                return;
            }

            changedValueIds = KeyFrameValueChangeDetector.CollectChangedValueIds(
                currentJson,
                animationJson);

            if (currentObject is KeyFrameAnimation currentKeyFrameAnimation)
            {
                // Existing keyframes already carry the owning validator, but their public setter
                // can only supply a default context. Populate the requested fields first, then run
                // the validator once below with the actual owner-property context.
                var validator = currentKeyFrameAnimation.Validator;
                currentKeyFrameAnimation.Validator = null;
                try
                {
                    ApplyCoreObject(currentObject, animationJson);
                }
                finally
                {
                    currentKeyFrameAnimation.Validator = validator;
                }
            }
            else
            {
                ApplyCoreObject(currentObject, animationJson);
            }
        }
        else
        {
            var animation = (IAnimation)CoreSerializer.DeserializeFromJsonObject(
                NormalizeCoreSerializableJson(animationJson, typeof(IAnimation)),
                typeof(IAnimation),
                CreateOptions(property.GetOwnerObject()));
            if (animation.ValueType != property.ValueType)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Animation value type '{animation.ValueType.FullName}' does not match property '{property.Name}' value type '{property.ValueType.FullName}'.",
                    property.Name));
            }

            // CoreSerializer appends keyframes in JSON order, but GetPreviousAndNextKeyFrame walks collection order.
            if (animation is KeyFrameAnimation keyFrameAnimation)
            {
                SortKeyFramesByKeyTime(keyFrameAnimation.KeyFrames);
            }

            property.Animation = animation;
        }

        RevalidateKeyFrameValues(property, changedValueIds);
    }

    private void RevalidateKeyFrameValues(
        IProperty property,
        IReadOnlySet<Guid>? changedValueIds)
    {
        if (property.Animation is not KeyFrameAnimation animation)
        {
            return;
        }

        CoreSerializerOptions options = CreateOptions(property.GetOwnerObject());
        foreach (IKeyFrame keyFrame in animation.KeyFrames)
        {
            if (changedValueIds is not null && !changedValueIds.Contains(keyFrame.Id))
            {
                continue;
            }

            object? value = keyFrame.Value;
            ValidationOutcome outcome = ValidationEvaluator.EvaluateAnimationValue(
                property,
                value,
                options,
                out object? acceptedValue);
            if (outcome.Status == ValidationStatus.Rejected)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Animation value for property '{property.Name}' is invalid: {outcome.Message}",
                    property.Name,
                    outcome.Hint));
            }

            // A newly deserialized animation receives the owning property's validator only when it
            // is attached. Evaluate once with that property's context, then store the accepted value
            // without invoking KeyFrame<T>'s contextless validator path a second time.
            AssignAcceptedKeyFrameValue(keyFrame, acceptedValue);
        }
    }

    private static void AssignAcceptedKeyFrameValue(IKeyFrame keyFrame, object? value)
    {
        if (keyFrame is not KeyFrame concreteKeyFrame)
        {
            keyFrame.Value = value;
            return;
        }

        var validator = concreteKeyFrame.Validator;
        concreteKeyFrame.Validator = null;
        try
        {
            keyFrame.Value = value;
        }
        finally
        {
            concreteKeyFrame.Validator = validator;
        }
    }

    private static void ApplyExpressions(EngineObject target, JsonObject desired)
    {
        JsonObject? expressions = desired.TryGetPropertyValue("Expressions", out JsonNode? expressionsNode)
            ? RequireObjectMember(expressionsNode, "Expressions")
            : null;

        if (expressions is not null)
        {
            RequireTargetProperties(target, expressions, "Expression", static property => property.SupportsExpression, "does not support expressions");
        }

        foreach (IProperty property in target.Properties.Where(property => property.SupportsExpression))
        {
            if (expressions?.TryGetPropertyValue(property.Name, out JsonNode? node) == true && node is not null)
            {
                property.DeserializeExpression(node);
            }
            else
            {
                property.Expression = null;
            }
        }
    }

    // Animations and Expressions are both keyed by property name; every key must name an existing
    // property that supports the member kind before any property is reset.
    private static void RequireTargetProperties(
        EngineObject target,
        JsonObject members,
        string memberKind,
        Func<IProperty, bool> isSupported,
        string unsupportedReason)
    {
        foreach (string name in members.Select(pair => pair.Key))
        {
            IProperty? property = target.Properties.FirstOrDefault(item => item.Name == name);
            if (property is null)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"{memberKind} target property '{name}' does not exist on '{target.GetType().FullName}'.",
                    target.Id.ToString()));
            }

            if (!isSupported(property))
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Property '{name}' {unsupportedReason}.",
                    target.Id.ToString()));
            }
        }
    }

    private void ApplyKeyFrameList(KeyFrames list, JsonArray desired, CoreObject? owner)
    {
        var desiredIds = new HashSet<Guid>();
        for (int index = 0; index < desired.Count; index++)
        {
            if (desired[index] is not JsonObject itemJson)
            {
                // Skipping a non-object entry would leave its Id uncollected, so the removal pass
                // below would then delete other existing keyframes — reject instead.
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"KeyFrames entry at index {index} is not an object.",
                    $"KeyFrames[{index}]",
                    "Each KeyFrames member must be a JSON object with an optional Id; remove null/primitive entries."));
            }

            CoreObject item;
            if (CollectionReconciler.TryGetId(itemJson, out Guid id) && FindById(list, id) is { } existing)
            {
                item = existing;
                ApplyCoreObject(existing, itemJson);
            }
            else
            {
                item = (CoreObject)CoreSerializer.DeserializeFromJsonObject(
                    NormalizeCoreSerializableJson(itemJson, typeof(IKeyFrame)),
                    typeof(IKeyFrame),
                    CreateOptions(owner));
                list.Add((IKeyFrame)item, out _);
            }

            desiredIds.Add(item.Id);
        }

        for (int index = list.Count - 1; index >= 0; index--)
        {
            if (!desiredIds.Contains(list[index].Id))
            {
                list.RemoveAt(index);
            }
        }

        SortKeyFramesByKeyTime(list);
    }

    // GetPreviousAndNextKeyFrame walks collection order, so a KeyTime edit on an existing frame that
    // now crosses a neighbour must re-sort the list or interpolation picks the wrong prev/next frames.
    private static void SortKeyFramesByKeyTime(KeyFrames list)
    {
        List<IKeyFrame> sorted = list.OrderBy(frame => frame.KeyTime).ToList();
        for (int target = 0; target < sorted.Count; target++)
        {
            int current = list.IndexOf(sorted[target]);
            if (current != target)
            {
                Move(list, current, target);
            }
        }
    }

    private static Easing DeserializeEasing(JsonNode node)
    {
        if (node is JsonValue value && value.TryGetValue(out string? typeName))
        {
            Type? type = ResolveEasingType(typeName);
            if (type is not null && Activator.CreateInstance(type) is Easing easing)
            {
                return easing;
            }

            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Unknown easing type '{typeName}'."));
        }

        if (node is JsonObject obj)
        {
            float x1 = obj.TryGetPropertyValue("X1", out JsonNode? x1Node) ? x1Node!.GetValue<float>() : 0;
            float y1 = obj.TryGetPropertyValue("Y1", out JsonNode? y1Node) ? y1Node!.GetValue<float>() : 0;
            float x2 = obj.TryGetPropertyValue("X2", out JsonNode? x2Node) ? x2Node!.GetValue<float>() : 1;
            float y2 = obj.TryGetPropertyValue("Y2", out JsonNode? y2Node) ? y2Node!.GetValue<float>() : 1;
            return new SplineEasing(x1, y1, x2, y2);
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            "Easing must be a type string or spline object."));
    }

    internal static string? ValidateEasingNode(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue(out string? typeName))
        {
            return ResolveEasingType(typeName) is not null ? null : $"Unknown easing type '{typeName}'.";
        }

        if (node is JsonObject)
        {
            return null;
        }

        return "Easing must be a type string or spline object.";
    }

    private static Type? ResolveEasingType(string typeName)
    {
        return Type.GetType(typeName)
               ?? typeof(Easing).Assembly.GetTypes().FirstOrDefault(candidate =>
                   candidate.IsAssignableTo(typeof(Easing))
                   && candidate.GetConstructor(Type.EmptyTypes) is not null
                   && IsEasingTypeMatch(candidate, typeName));
    }

    private static bool IsEasingTypeMatch(Type candidate, string typeName)
    {
        return candidate.Name == typeName
               || candidate.FullName == typeName
               || typeName.EndsWith($":{candidate.Name}", StringComparison.Ordinal)
               || typeName.EndsWith($".{candidate.Name}", StringComparison.Ordinal);
    }
}
