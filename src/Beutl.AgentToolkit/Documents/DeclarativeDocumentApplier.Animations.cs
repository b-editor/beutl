using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Collections;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Documents;

internal sealed partial class DeclarativeDocumentApplier
{
    private void ApplyKeyFrameAnimation(IKeyFrameAnimation animation, JsonObject desired)
    {
        var target = (CoreObject)animation;
        JsonObject payload = (JsonObject)desired.DeepClone();
        payload.Remove(nameof(IKeyFrameAnimation.KeyFrames));
        NormalizeRegisteredPropertyValues(target, payload);

        CoreSerializer.PopulateFromJsonObject(target, target.GetType(), payload, CreateOptions(target));
        ClearAbsentRegisteredObjectProperties(target, desired, payload);

        if (desired.TryGetPropertyValue(nameof(IKeyFrameAnimation.KeyFrames), out JsonNode? keyframesNode))
        {
            ApplyKeyFrameList(animation.KeyFrames, RequireArrayMember(keyframesNode, nameof(IKeyFrameAnimation.KeyFrames)), target);
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
            && keyFrameObject.Properties.FirstOrDefault(property => property.Name == nameof(KeyFrame<float>.Value)) is { } valueProperty)
        {
            keyFrame.Value = valueNode is null
                ? null
                : EnumJsonValueNormalizer.Deserialize(valueNode, valueProperty.ValueType, CreateOptions(keyFrameObject));
        }

        if (desired.TryGetPropertyValue(nameof(KeyFrame.Easing), out JsonNode? easingNode) && easingNode is not null)
        {
            JsonObject current = CoreSerializer.SerializeToJsonObject(keyFrame, CreateOptions(keyFrame as CoreObject));
            // Recovered easings retain their original JSON even while evaluating as Linear.
            // A different serialized value is an explicit repair, including a new Linear easing.
            if (!JsonNode.DeepEquals(current[nameof(KeyFrame.Easing)], easingNode))
            {
                keyFrame.Easing.Value = DeserializeEasing(easingNode);
            }
        }

        if (desired.TryGetPropertyValue(nameof(KeyFrame.KeyTime), out JsonNode? keyTimeNode) && keyTimeNode is not null)
        {
            keyFrame.KeyTime.Value = (MediaTime)CoreSerializer.DeserializeFromJsonNode(
                keyTimeNode.DeepClone(),
                typeof(MediaTime),
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
            RequireTargetProperties(target, animations, "Animation", static property => property is IExpressionProperty { IsAnimatable: true }, "is not animatable");
        }

        foreach (IProperty property in target.Properties.Where(property => property is IExpressionProperty { IsAnimatable: true }))
        {
            if (animations?.TryGetPropertyValue(property.Name, out JsonNode? node) == true && node is JsonObject animationJson)
            {
                ApplyAnimation(property, animationJson);
            }
            else
            {
                property.SetKeyFrameAnimation(null);
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
        IKeyFrameAnimation? current = property.GetKeyFrameAnimation();
        if (current is CoreObject currentObject
            && IdentityMatches(currentObject, animationJson)
            && TypeMatches(currentObject, animationJson))
        {
            // A merge patch sends the whole document; do not replay unchanged animations, so pre-existing
            // values are not re-validated or coerced.
            JsonObject currentJson = SerializeCurrent(currentObject);
            if (JsonNode.DeepEquals(currentJson, animationJson))
            {
                return;
            }

            changedValueIds = KeyFrameValueChangeDetector.CollectChangedValueIds(
                currentJson,
                animationJson);

            if (currentObject is IKeyFrameAnimation currentKeyFrameAnimation)
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
            var animation = (IKeyFrameAnimation)CoreSerializer.DeserializeFromJsonObject(
                NormalizeCoreSerializableJson(animationJson, typeof(IKeyFrameAnimation)),
                typeof(IKeyFrameAnimation),
                CreateOptions(property.GetOwnerObject()));
            if (animation.ValueType != property.ValueType)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Animation value type '{animation.ValueType.FullName}' does not match property '{property.Name}' value type '{property.ValueType.FullName}'.",
                    property.Name));
            }

            // CoreSerializer appends keyframes in JSON order, but GetPreviousAndNextKeyFrame walks collection order.
            if (animation is IKeyFrameAnimation keyFrameAnimation)
            {
                SortKeyFramesByKeyTime(keyFrameAnimation.KeyFrames);
            }

            property.SetKeyFrameAnimation(animation);
        }

        RevalidateKeyFrameValues(property, changedValueIds);
    }

    private void RevalidateKeyFrameValues(
        IProperty property,
        IReadOnlySet<Guid>? changedValueIds)
    {
        if (property.GetKeyFrameAnimation() is not IKeyFrameAnimation animation)
        {
            return;
        }

        CoreSerializerOptions options = CreateOptions(property.GetOwnerObject());
        foreach (IKeyFrame keyFrame in animation.KeyFrames)
        {
            if (changedValueIds is not null && !changedValueIds.Contains(keyFrame.Id.Value))
            {
                continue;
            }

            object? value = keyFrame.Value;
            ValidationOutcome outcome = ValidationEvaluator.Evaluate(
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
            RequireTargetProperties(target, expressions, "Expression", static property => property is IExpressionProperty, "does not support expressions");
        }

        foreach (IExpressionProperty property in target.Properties.OfType<IExpressionProperty>())
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
            // A base field holds only a value and takes neither.
            IProperty? property = target.Properties.FirstOrDefault(
                item => item.Name == name && !target.IsBaseField(item));
            if (property is null)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"{memberKind} target property '{name}' does not exist on '{target.GetType().FullName}'.",
                    target.Id.Value.ToString()));
            }

            if (!isSupported(property))
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Property '{name}' {unsupportedReason}.",
                    target.Id.Value.ToString()));
            }
        }
    }

    private void ApplyKeyFrameList(ICoreList<IKeyFrame> list, JsonArray desired, CoreObject? owner)
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

            desiredIds.Add(item.Id.Value);
        }

        for (int index = list.Count - 1; index >= 0; index--)
        {
            if (!desiredIds.Contains(list[index].Id.Value))
            {
                list.RemoveAt(index);
            }
        }

        SortKeyFramesByKeyTime(list);
    }

    // GetPreviousAndNextKeyFrame walks collection order, so a KeyTime edit on an existing frame that
    // now crosses a neighbour must re-sort the list or interpolation picks the wrong prev/next frames.
    private static void SortKeyFramesByKeyTime(ICoreList<IKeyFrame> list)
    {
        List<IKeyFrame> sorted = list.OrderBy(frame => frame.KeyTime.Value).ToList();
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
