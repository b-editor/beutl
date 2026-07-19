using System.Collections;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Documents;

internal sealed class DeclarativeDocumentApplier
{
    // Fallback base for objects not attached to the hierarchy yet (newly created identity-list members),
    // whose own ancestry cannot supply the Uri their relative file sources were written against.
    private Uri? _documentBaseUri;

    public void Apply(CoreObject root, JsonObject document)
    {
        _documentBaseUri = ResolveBaseUri(root);
        ApplyCoreObject(root, document);
    }

    private void ApplyCoreObject(CoreObject target, JsonObject desired)
    {
        switch (target)
        {
            case Scene scene:
                ApplyScene(scene, desired);
                break;
            case Element element:
                ApplyElement(element, desired);
                break;
            case EngineObject engineObject:
                ApplyEngineObject(engineObject, desired);
                break;
            case KeyFrameAnimation animation:
                ApplyKeyFrameAnimation(animation, desired);
                break;
            case IKeyFrame keyFrame:
                ApplyKeyFrame(keyFrame, desired);
                break;
            default:
                JsonObject payload = (JsonObject)desired.DeepClone();
                NormalizeRegisteredPropertyValues(target, payload);
                CoreSerializer.PopulateFromJsonObject(target, target.GetType(), payload, CreateOptions(target));
                break;
        }
    }

    private void ApplyScene(Scene scene, JsonObject desired)
    {
        ApplyRegisteredProperties(
            scene,
            desired,
            new HashSet<string> { nameof(Scene.Children), nameof(Scene.FrameSize), "Groups" });

        int width = desired.TryGetPropertyValue("Width", out JsonNode? widthNode)
            ? widthNode!.GetValue<int>()
            : scene.FrameSize.Width;
        int height = desired.TryGetPropertyValue("Height", out JsonNode? heightNode)
            ? heightNode!.GetValue<int>()
            : scene.FrameSize.Height;
        scene.FrameSize = new PixelSize(width, height);

        if (desired.TryGetPropertyValue("Elements", out JsonNode? elementsNode))
        {
            ApplyIdentityList(scene.Children, typeof(Element), "Elements", RequireArrayMember(elementsNode, "Elements"), scene);
        }
        else
        {
            scene.Children.Clear();
        }

        if (desired.TryGetPropertyValue("Groups", out JsonNode? groupsNode))
        {
            scene.Groups.Clear();
            foreach (string group in groupsNode?.Deserialize<string[]>() ?? [])
            {
                HashSet<Guid> ids = group
                    .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(text => Guid.TryParse(text, out Guid id) ? id : Guid.Empty)
                    .Where(id => id != Guid.Empty && scene.Children.Any(element => element.Id == id))
                    .ToHashSet();
                if (ids.Count >= 2)
                {
                    scene.Groups.Add(ids.ToImmutableHashSet());
                }
            }
        }
        else
        {
            // A full desired document that omits Groups means "no groups"; clear the existing ones like
            // Elements/Objects do, so the plan's removal is not left stale on later saves/renders.
            scene.Groups.Clear();
        }
    }

    private void ApplyElement(Element element, JsonObject desired)
    {
        JsonObject payload = (JsonObject)desired.DeepClone();
        payload.Remove(nameof(Element.Objects));
        // Storage URIs are toolkit-managed sidecar paths; never let a desired document redirect an
        // existing element's .belm outside the workspace.
        payload.Remove("Uri");
        NormalizeRegisteredPropertyValues(element, payload);

        CoreSerializer.PopulateFromJsonObject(element, element.GetType(), payload, CreateOptions(element));
        ClearAbsentRegisteredObjectProperties(element, desired, payload);

        if (desired.TryGetPropertyValue(nameof(Element.Objects), out JsonNode? objectsNode))
        {
            ApplyIdentityList(element.Objects, typeof(EngineObject), "Objects", RequireArrayMember(objectsNode, "Objects"), element);
        }
        else
        {
            element.Objects.Clear();
        }
    }

    private void ApplyEngineObject(EngineObject target, JsonObject desired)
    {
        JsonObject payload = (JsonObject)desired.DeepClone();
        payload.Remove("Animations");
        payload.Remove("Expressions");
        payload.Remove("Uri");

        foreach (IListProperty listProperty in target.Properties.OfType<IListProperty>())
        {
            payload.Remove(listProperty.Name);
        }

        NormalizeRegisteredPropertyValues(target, payload);
        NormalizeEnginePropertyValues(target, payload);
        CoreSerializer.PopulateFromJsonObject(target, target.GetType(), payload, CreateOptions(target));
        ClearAbsentRegisteredObjectProperties(target, desired, payload);
        ClearAbsentObjectProperties(target, desired, payload);
        ApplyListProperties(target, desired);
        ApplyAnimations(target, desired);
        ApplyExpressions(target, desired);
    }

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
            keyFrame.Easing = DeserializeEasing(easingNode);
        }

        if (desired.TryGetPropertyValue(nameof(KeyFrame.KeyTime), out JsonNode? keyTimeNode) && keyTimeNode is not null)
        {
            keyFrame.KeyTime = (TimeSpan)CoreSerializer.DeserializeFromJsonNode(
                keyTimeNode.DeepClone(),
                typeof(TimeSpan),
                CreateOptions(keyFrame as CoreObject))!;
        }
    }

    private void ApplyRegisteredProperties(CoreObject target, JsonObject desired, IReadOnlySet<string> excluded)
    {
        foreach (CoreProperty property in PropertyRegistry.GetRegistered(target.GetType()))
        {
            if (excluded.Contains(property.Name)
                || !desired.TryGetPropertyValue(property.Name, out JsonNode? valueNode))
            {
                continue;
            }

            object? value = valueNode is null
                ? null
                : EnumJsonValueNormalizer.Deserialize(valueNode, property.PropertyType, CreateOptions(target));
            target.SetValue(property, value);
        }
    }

    private static void NormalizeRegisteredPropertyValues(CoreObject target, JsonObject payload)
    {
        foreach (CoreProperty property in PropertyRegistry.GetRegistered(target.GetType()))
        {
            NormalizePropertyValue(payload, property.Name, property.PropertyType);
        }
    }

    private static void NormalizeEnginePropertyValues(EngineObject target, JsonObject payload)
    {
        foreach (IProperty property in target.Properties)
        {
            NormalizePropertyValue(payload, property.Name, property.ValueType);
        }
    }

    private static void NormalizePropertyValue(JsonObject payload, string propertyName, Type valueType)
    {
        if (payload.TryGetPropertyValue(propertyName, out JsonNode? valueNode) && valueNode is not null)
        {
            payload[propertyName] = EnumJsonValueNormalizer.Normalize(valueNode, valueType);
        }
    }

    private static void ClearAbsentRegisteredObjectProperties(
        CoreObject target,
        JsonObject desired,
        JsonObject serializedPayload)
    {
        foreach (CoreProperty property in PropertyRegistry.GetRegistered(target.GetType()))
        {
            // Getter-only properties (e.g. Element.Objects) reject SetValue, and identity lists
            // (Objects/KeyFrames) are cleared by their specialized handlers, never by nulling.
            if (property.PropertyType.IsValueType
                || property.PropertyType == typeof(string)
                || typeof(ICoreList).IsAssignableFrom(property.PropertyType)
                || property is IStaticProperty { CanWrite: false }
                || desired.ContainsKey(property.Name)
                || serializedPayload.ContainsKey(property.Name))
            {
                continue;
            }

            target.SetValue(property, null);
        }
    }

    private static void ClearAbsentObjectProperties(
        EngineObject target,
        JsonObject desired,
        JsonObject serializedPayload)
    {
        foreach (IProperty property in target.Properties)
        {
            if (property is IListProperty
                || property.ValueType.IsValueType
                || property.ValueType == typeof(string)
                || desired.ContainsKey(property.Name)
                || serializedPayload.ContainsKey(property.Name))
            {
                continue;
            }

            property.CurrentValue = null;
        }
    }

    private void ApplyListProperties(EngineObject target, JsonObject desired)
    {
        foreach (IListProperty listProperty in target.Properties.OfType<IListProperty>())
        {
            if (desired.TryGetPropertyValue(listProperty.Name, out JsonNode? node))
            {
                ApplyIdentityList(listProperty, listProperty.ElementType, listProperty.Name, RequireArrayMember(node, listProperty.Name), target);
            }
            else
            {
                listProperty.Clear();
            }
        }
    }

    private void ApplyAnimations(EngineObject target, JsonObject desired)
    {
        JsonObject? animations = desired.TryGetPropertyValue("Animations", out JsonNode? animationsNode)
            ? RequireObjectMember(animationsNode, "Animations")
            : null;

        if (animations is not null)
        {
            foreach (string name in animations.Select(pair => pair.Key))
            {
                IProperty? property = target.Properties.FirstOrDefault(item => item.Name == name);
                if (property is null)
                {
                    throw new ReconcileException(new ToolError(
                        ErrorCode.ValidationRejected,
                        $"Animation target property '{name}' does not exist on '{target.GetType().FullName}'.",
                        target.Id.ToString()));
                }

                if (!property.IsAnimatable)
                {
                    throw new ReconcileException(new ToolError(
                        ErrorCode.ValidationRejected,
                        $"Property '{name}' is not animatable.",
                        target.Id.ToString()));
                }
            }
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
        IAnimation? current = property.Animation;
        if (current is CoreObject currentObject
            && IdentityMatches(currentObject, animationJson)
            && TypeMatches(currentObject, animationJson))
        {
            ApplyCoreObject(currentObject, animationJson);
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
    }

    private static void ApplyExpressions(EngineObject target, JsonObject desired)
    {
        JsonObject? expressions = desired.TryGetPropertyValue("Expressions", out JsonNode? expressionsNode)
            ? RequireObjectMember(expressionsNode, "Expressions")
            : null;

        if (expressions is not null)
        {
            foreach (string name in expressions.Select(pair => pair.Key))
            {
                IProperty? property = target.Properties.FirstOrDefault(item => item.Name == name);
                if (property is null)
                {
                    throw new ReconcileException(new ToolError(
                        ErrorCode.ValidationRejected,
                        $"Expression target property '{name}' does not exist on '{target.GetType().FullName}'.",
                        target.Id.ToString()));
                }

                if (!property.SupportsExpression)
                {
                    throw new ReconcileException(new ToolError(
                        ErrorCode.ValidationRejected,
                        $"Property '{name}' does not support expressions.",
                        target.Id.ToString()));
                }
            }
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

    private void ApplyIdentityList(IList list, Type elementBaseType, string fieldName, JsonArray desired, CoreObject? owner)
    {
        if (!IsIdentityArray(desired))
        {
            ReplaceList(list, elementBaseType, fieldName, desired, owner);
            return;
        }

        var desiredIds = new HashSet<Guid>();
        for (int desiredIndex = 0; desiredIndex < desired.Count; desiredIndex++)
        {
            if (desired[desiredIndex] is not JsonObject itemJson)
            {
                // Silently skipping a non-object entry would leave its Id uncollected, so the
                // removal pass below would then delete other existing children — reject instead.
                string entryPath = CreateIdentityListItemPath(fieldName, desiredIndex);
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Identity array entry at '{entryPath}' is not an object.",
                    entryPath,
                    "Each identity-array member must be a JSON object with an optional Id; remove null/primitive entries."));
            }

            CoreObject item;
            string itemPath = CreateIdentityListItemPath(fieldName, desiredIndex);
            try
            {
                if (TryGetId(itemJson, out Guid id) && FindById(list, id) is { } existing)
                {
                    item = existing;
                    ApplyCoreObject(existing, itemJson);
                }
                else
                {
                    item = CreateIdentityListItem(itemJson, elementBaseType, owner);
                    if (owner is Scene scene && item is Element element)
                    {
                        JsonObject elementJson = (JsonObject)itemJson.DeepClone();
                        elementJson.Remove("Uri");
                        // The subtree's relative media URIs were written against the incoming
                        // element's own .belm, which may sit in a subdirectory of the scene. That
                        // path only survives in the JSON: the element is still detached here, and
                        // AssignNewElementUri later rehomes it directly under the scene.
                        Uri? incomingBaseUri = ResolveIncomingElementBaseUri(scene, itemJson);
                        ApplyDetached(element, elementJson, incomingBaseUri);
                        AssignNewElementUri(scene, element);
                    }
                    else
                    {
                        // Same detachment as above: the item is populated before insertion, so it
                        // cannot reach the owner through HierarchicalParent.
                        ApplyDetached(item, itemJson, ResolveBaseUri(owner));
                    }
                }
            }
            catch (Exception ex) when (ex is not ReconcileException)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Desired document produced a fallback object or invalid serialized object at '{itemPath}': {ex.Message}",
                    itemPath,
                    "Call get_schema for the concrete type, then retry apply_edit with serialized property shapes returned by the schema. Objects require concrete EngineObject discriminators from get_schema; typed property values such as Pen, Brush, Transform, Effect, and Animation also require concrete schema-returned object shapes."));
            }

            desiredIds.Add(item.Id);
            int currentIndex = IndexOfReference(list, item);
            if (currentIndex < 0)
            {
                if (owner is Scene scene && item is Element { Uri: null } element)
                {
                    AssignNewElementUri(scene, element);
                }

                list.Insert(Math.Min(desiredIndex, list.Count), item);
            }
            else if (currentIndex != desiredIndex)
            {
                Move(list, currentIndex, desiredIndex);
            }
        }

        for (int index = list.Count - 1; index >= 0; index--)
        {
            if (list[index] is CoreObject item && !desiredIds.Contains(item.Id))
            {
                list.RemoveAt(index);
            }
        }

        ValidateFlowOperatorPortalPairing(owner, list);
    }

    // Element.AddObject/InsertObject always pair a PortalObject before a flow operator — it is the
    // content feed the operator renders. Validate the FINAL order (not just inserts) so a patch that
    // reorders an existing [PortalObject, DrawableGroup] into an invalid render chain is rejected too.
    private static void ValidateFlowOperatorPortalPairing(CoreObject? owner, IList list)
    {
        if (owner is not Element)
        {
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is IFlowOperator
                && (i == 0 || list[i - 1] is not PortalObject))
            {
                CoreObject flowOperator = (CoreObject)list[i]!;
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Flow operator '{flowOperator.GetType().Name}' in Objects is not preceded by a PortalObject.",
                    flowOperator.Id.ToString(),
                    "Keep a PortalObject entry immediately before each flow operator in Objects (Element.AddObject pairs them); do not reorder or delete the portal out of the pair."));
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
            if (TryGetId(itemJson, out Guid id) && FindById(list, id) is { } existing)
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

    private void ReplaceList(IList list, Type elementBaseType, string fieldName, JsonArray desired, CoreObject? owner)
    {
        bool typedObjectList = typeof(ICoreSerializable).IsAssignableFrom(elementBaseType);
        // Validate and deserialize every entry before mutating the target: DocumentAdapter.Write can run
        // outside a HistoryManager transaction, so a mid-loop throw must not leave the list cleared or
        // half-rebuilt. Only clear/add once the full replacement is known to be valid.
        var items = new List<object?>(desired.Count);
        for (int index = 0; index < desired.Count; index++)
        {
            JsonNode? node = desired[index];
            if (typedObjectList && node is not JsonObject)
            {
                // A wholesale replacement of an object list must not insert a null or primitive member:
                // the collection accepts it, then rendering/audio paths dereference the null element.
                string entryPath = CreateIdentityListItemPath(fieldName, index);
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"List entry at '{entryPath}' is not an object.",
                    entryPath,
                    "Each member of this object list must be a JSON object; remove null/primitive entries."));
            }

            if (node is null)
            {
                items.Add(null);
                continue;
            }

            items.Add(node is JsonObject obj && typedObjectList
                ? CoreSerializer.DeserializeFromJsonObject(
                    NormalizeCoreSerializableJson(obj, elementBaseType),
                    elementBaseType,
                    CreateOptions(owner))
                : EnumJsonValueNormalizer.Deserialize(node, elementBaseType, CreateOptions(owner)));
        }

        list.Clear();
        foreach (object? item in items)
        {
            list.Add(item);
        }
    }

    private static string CreateIdentityListItemPath(string fieldName, int index)
        => $"{fieldName}[{index}]";

    // A present but non-array child-list value is a malformed document, not an intentional omission:
    // treating it as "clear" (the absent-property branch) would silently erase the whole list on a typo.
    private static JsonArray RequireArrayMember(JsonNode? node, string fieldName)
    {
        if (node is JsonArray array)
        {
            return array;
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            $"'{fieldName}' must be a JSON array.",
            fieldName,
            $"Provide '{fieldName}' as an array of members, or omit it entirely to clear the list."));
    }

    // A present but non-object map value is a malformed document, not an intentional omission: treating
    // it as "clear" (the absent-property branch) would silently wipe every animation/expression on a typo.
    private static JsonObject RequireObjectMember(JsonNode? node, string fieldName)
    {
        if (node is JsonObject obj)
        {
            return obj;
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            $"'{fieldName}' must be a JSON object.",
            fieldName,
            $"Provide '{fieldName}' as an object keyed by property name, or omit it entirely to clear it."));
    }

    private CoreObject CreateIdentityListItem(JsonObject itemJson, Type elementBaseType, CoreObject? owner)
    {
        var shell = new JsonObject();
        CopyIfPresent(itemJson, shell, "$type");
        CopyIfPresent(itemJson, shell, nameof(CoreObject.Id));

        return (CoreObject)CoreSerializer.DeserializeFromJsonObject(
            NormalizeCoreSerializableJson(shell, elementBaseType),
            elementBaseType,
            CreateOptions(owner));
    }

    private static void CopyIfPresent(JsonObject source, JsonObject destination, string propertyName)
    {
        if (source.TryGetPropertyValue(propertyName, out JsonNode? node))
        {
            destination[propertyName] = node?.DeepClone();
        }
    }

    private static JsonObject NormalizeCoreSerializableJson(JsonObject json, Type baseType)
    {
        JsonObject normalized = (JsonObject)EnumJsonValueNormalizer.Normalize(json, baseType);
        Type? actualType = baseType.IsSealed ? baseType : normalized.GetDiscriminator(baseType);
        if (actualType is null)
        {
            return normalized;
        }

        if (typeof(Scene).IsAssignableFrom(actualType))
        {
            NormalizeIdentityArray(normalized, nameof(Scene.Children), typeof(Element));
        }
        else if (typeof(Element).IsAssignableFrom(actualType))
        {
            NormalizeIdentityArray(normalized, nameof(Element.Objects), typeof(EngineObject));
        }
        else if (typeof(KeyFrameAnimation).IsAssignableFrom(actualType))
        {
            NormalizeIdentityArray(normalized, nameof(KeyFrameAnimation.KeyFrames), typeof(IKeyFrame));
        }

        return normalized;
    }

    private static void NormalizeIdentityArray(JsonObject obj, string propertyName, Type elementBaseType)
    {
        if (!obj.TryGetPropertyValue(propertyName, out JsonNode? node) || node is not JsonArray array)
        {
            return;
        }

        var normalizedArray = new JsonArray();
        foreach (JsonNode? item in array)
        {
            normalizedArray.Add(item is JsonObject child
                ? NormalizeCoreSerializableJson(child, elementBaseType)
                : item?.DeepClone());
        }

        obj[propertyName] = normalizedArray;
    }

    private static void AssignNewElementUri(Scene scene, Element element)
    {
        if (scene.Uri is null)
        {
            throw new InvalidOperationException("Scene must have a Uri before elements can be inserted.");
        }

        // Assign a URI value only. This runs inside the validation sandbox (a dry-run) and before the
        // apply-time rehome, so it must not touch the filesystem; the directory is created at Save.
        string sceneDirectory = Path.GetDirectoryName(scene.Uri.LocalPath)
                                ?? throw new InvalidOperationException("Scene Uri must have a directory.");

        string path = Path.Combine(sceneDirectory, $"{element.Id:N}.{EditorConstants.ElementFileExtension}");
        for (int index = 1; File.Exists(path) || scene.Children.Any(item => item.Uri?.LocalPath == path); index++)
        {
            path = Path.Combine(sceneDirectory, $"{element.Id:N}-{index}.{EditorConstants.ElementFileExtension}");
        }

        element.Uri = new Uri(path);
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

    private static CoreObject? FindById(IList list, Guid id)
    {
        foreach (object? item in list)
        {
            if (item is CoreObject coreObject && coreObject.Id == id)
            {
                return coreObject;
            }
        }

        return null;
    }

    private static bool IsIdentityArray(JsonArray array)
    {
        return array.OfType<JsonObject>().Any(item => item.ContainsKey(nameof(CoreObject.Id)));
    }

    private static bool TryGetId(JsonObject obj, out Guid id)
    {
        id = default;
        return obj.TryGetPropertyValue(nameof(CoreObject.Id), out JsonNode? idNode)
               && idNode?.GetValue<string>() is { } idText
               && Guid.TryParse(idText, out id);
    }

    private static bool IdentityMatches(CoreObject current, JsonObject desired)
    {
        return !TryGetId(desired, out Guid id) || current.Id == id;
    }

    private static bool TypeMatches(CoreObject current, JsonObject desired)
    {
        return !desired.TryGetPropertyValue("$type", out JsonNode? typeNode)
               || typeNode?.GetValue<string>() == IdentityHelper.WriteDiscriminator(current.GetType());
    }

    private static int IndexOfReference(IList list, object item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    private static void Move(IList list, int oldIndex, int newIndex)
    {
        if (list is ICoreList coreList)
        {
            coreList.Move(oldIndex, Math.Clamp(newIndex, 0, list.Count - 1));
            return;
        }

        object? item = list[oldIndex];
        list.RemoveAt(oldIndex);
        list.Insert(Math.Clamp(newIndex, 0, list.Count), item);
    }

    // Applies a subtree whose relative file sources were written against a base other than the
    // document root's, for objects that cannot reach that base through HierarchicalParent.
    private void ApplyDetached(CoreObject target, JsonObject desired, Uri? baseUri)
    {
        Uri? previous = _documentBaseUri;
        _documentBaseUri = baseUri ?? previous;
        try
        {
            ApplyCoreObject(target, desired);
        }
        finally
        {
            _documentBaseUri = previous;
        }
    }

    private static Uri? ResolveIncomingElementBaseUri(Scene scene, JsonObject itemJson)
    {
        return itemJson["Uri"] is JsonValue value
               && value.TryGetValue(out string? relative)
               && Uri.TryCreate(scene.Uri, Uri.UnescapeDataString(relative), out Uri? uri)
            ? uri
            : null;
    }

    private CoreSerializerOptions CreateOptions(CoreObject? target)
    {
        return CreateOptions(ResolveBaseUri(target) ?? _documentBaseUri);
    }

    internal static CoreSerializerOptions CreateOptions(Uri? baseUri)
    {
        return new CoreSerializerOptions
        {
            BaseUri = baseUri,
            Mode = CoreSerializationMode.Read | CoreSerializationMode.EmbedReferencedObjects
        };
    }

    // Mirrors the serializer's context chain: a nested object inherits the BaseUri of the nearest
    // ancestor that owns a Uri, which is what its relative file-source URIs were written against.
    internal static Uri? ResolveBaseUri(CoreObject? target)
    {
        if (target?.Uri is { } own)
        {
            return own;
        }

        for (IHierarchical? parent = (target as IHierarchical)?.HierarchicalParent;
             parent is not null;
             parent = parent.HierarchicalParent)
        {
            if (parent is CoreObject { Uri: not null } owner)
            {
                return owner.Uri;
            }
        }

        return null;
    }
}
