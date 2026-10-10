using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Animation;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Documents;

internal sealed partial class DeclarativeDocumentApplier
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
        // A present-but-null Markers member is a malformed document (RequireArrayMember rejects it,
        // like Elements); only actual omission clears the list. Validate before the first mutation:
        // ApplyScene applies in steps, so a late throw would leave the earlier steps applied on a
        // direct Documents.Write (the reconciler's sandbox covers apply_edit, not direct writes).
        JsonArray? markersArray = null;
        if (desired.TryGetPropertyValue(nameof(Scene.Markers), out JsonNode? markersNode))
        {
            markersArray = RequireArrayMember(markersNode, nameof(Scene.Markers));
            for (int index = 0; index < markersArray.Count; index++)
            {
                if (markersArray[index] is not JsonObject)
                {
                    string entryPath = CreateIdentityListItemPath(nameof(Scene.Markers), index);
                    throw new ReconcileException(new ToolError(
                        ErrorCode.ValidationRejected,
                        $"List entry at '{entryPath}' is not an object.",
                        entryPath,
                        "Each Markers member must be a JSON object; remove null/primitive entries."));
                }
            }
        }

        ApplyRegisteredProperties(
            scene,
            desired,
            new HashSet<string> { nameof(Scene.Children), nameof(Scene.FrameSize), "Groups", nameof(Scene.Markers) });

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

        // Markers is [NotAutoSerialized] but custom-serialized by Scene, so the generic registered-
        // property pass (which honors ShouldSerialize) never applies it — reconcile it by Id like
        // Elements/Objects, with the same authoritative-omission semantics.
        if (markersArray is not null)
        {
            ApplyIdentityList(scene.Markers, typeof(SceneMarker), nameof(Scene.Markers), markersArray, scene);
        }
        else
        {
            scene.Markers.Clear();
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
        RemoveUnchangedStructuredValues(element, payload);

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
        RemoveUnchangedStructuredValues(target, payload);
        CoreSerializer.PopulateFromJsonObject(target, target.GetType(), payload, CreateOptions(target));
        ClearAbsentRegisteredObjectProperties(target, desired, payload);
        ClearAbsentObjectProperties(target, desired, payload);
        ApplyListProperties(target, desired);
        ApplyAnimations(target, desired);
        ApplyExpressions(target, desired);
    }

    private void ApplyRegisteredProperties(CoreObject target, JsonObject desired, IReadOnlySet<string> excluded)
    {
        foreach (CoreProperty property in PropertyRegistry.GetRegistered(target.GetType()))
        {
            if (excluded.Contains(property.Name)
                || !IsSerializedProperty(target, property)
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

    // A merge patch is resolved into a full desired document, so every object reaches the applier on
    // every apply_edit. Deserializing a structured value (brush, pen, transform, effect group) always
    // yields a new instance, which the setter records as a replacement even when nothing changed.
    // Drop the members whose JSON equals the current serialization so they keep their instances.
    // Only members the base Deserialize sets when present are dropped: custom keys such as
    // Element.EnterTransition are assigned unconditionally, so omitting them would clear them.
    private void RemoveUnchangedStructuredValues(CoreObject target, JsonObject payload)
    {
        // A fallback replaces its Json with the payload it is populated from and saves that Json
        // verbatim, so it must see every member.
        if (target is IFallback)
        {
            return;
        }

        JsonObject? current = null;
        foreach (KeyValuePair<string, JsonNode?> pair in payload.ToArray())
        {
            if (pair.Value is not (JsonObject or JsonArray) || !IsPopulatedOnlyWhenPresent(target, pair.Key))
            {
                continue;
            }

            current ??= SerializeCurrent(target);
            if (current.TryGetPropertyValue(pair.Key, out JsonNode? currentValue)
                && JsonNode.DeepEquals(currentValue, pair.Value))
            {
                payload.Remove(pair.Key);
            }
        }
    }

    private static bool IsPopulatedOnlyWhenPresent(CoreObject target, string propertyName)
    {
        if (PropertyRegistry.FindRegistered(target, propertyName) is { } property)
        {
            return IsSerializedProperty(target, property);
        }

        return target is EngineObject engineObject
               && engineObject.Properties.Any(property => property.Name == propertyName && property is not IListProperty);
    }

    // Serializes the live object as Documents.Read wrote it into the document being applied, so its
    // members can be compared with the desired JSON.
    private JsonObject SerializeCurrent(CoreObject target)
    {
        return CoreSerializer.SerializeToJsonObject(
            target,
            new CoreSerializerOptions
            {
                BaseUri = ResolveBaseUri(target) ?? _documentBaseUri,
                Mode = CoreSerializationMode.EmbedReferencedObjects
            });
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
            // Non-serialized properties (e.g. Hierarchical.HierarchicalParent) never appear in a
            // document, so their absence carries no clear intent; nulling HierarchicalParent would
            // silently detach the subtree from the hierarchy root.
            if (property.PropertyType.IsValueType
                || property.PropertyType == typeof(string)
                || typeof(ICoreList).IsAssignableFrom(property.PropertyType)
                || property is IStaticProperty { CanWrite: false }
                || !IsSerializedProperty(target, property)
                || desired.ContainsKey(property.Name)
                || serializedPayload.ContainsKey(property.Name))
            {
                continue;
            }

            target.SetValue(property, null);
        }
    }

    private static bool IsSerializedProperty(CoreObject target, CoreProperty property)
    {
        return property.GetMetadata<CorePropertyMetadata>(target.GetType()).ShouldSerialize;
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

    private static bool IdentityMatches(CoreObject current, JsonObject desired)
    {
        return !CollectionReconciler.TryGetId(desired, out Guid id) || current.Id == id;
    }

    private static bool TypeMatches(CoreObject current, JsonObject desired)
    {
        return !desired.TryGetPropertyValue("$type", out JsonNode? typeNode)
               || typeNode?.GetValue<string>() == IdentityHelper.WriteDiscriminator(current.GetType());
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

    private static Uri? ResolveIncomingBaseUri(Uri? parentBaseUri, JsonObject itemJson)
    {
        return itemJson["Uri"] is JsonValue value
               && value.TryGetValue(out string? relative)
               && Uri.TryCreate(parentBaseUri, Uri.UnescapeDataString(relative), out Uri? uri)
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
