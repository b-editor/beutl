using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Animation;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.JsonConverters;
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
        // Compared with the document as DocumentAdapter.Read wrote it; its times read on the project's frame grid.
        using (MediaTimeJsonConverter.WriteTimeSpanStrings())
        using ((root as IHierarchical)?.FindHierarchicalParent<Project>() is { } project
                   ? MediaTimeJsonConverter.ReadOnFrameGrid(project.GetFrameRate())
                   : null)
        {
            ApplyCoreObject(root, document);
        }
    }

    private void ApplyCoreObject(CoreObject target, JsonObject desired)
    {
        if (target is IFallback fallback)
        {
            ApplyFallback(fallback, desired);
            return;
        }

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
            case IKeyFrameAnimation animation:
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

    // A fallback stands in for an object whose type could not be loaded and saves the JSON it was read from,
    // which is what the document shows for it, with the Uri of its own file when it has one. There is nothing
    // Beutl knows in it to apply, and populating it would replace that JSON with the payload, so an agent can
    // leave it as it is or remove it, but not edit it.
    private void ApplyFallback(IFallback fallback, JsonObject desired)
    {
        if (JsonNode.DeepEquals(WithoutMetadata(desired), WithoutMetadata(fallback.Json))
            && StaysInItsFile(fallback, desired))
        {
            return;
        }

        // The type name comes from the file, so the message leaves it out.
        string target = fallback is CoreObject { Id.Value: var id } ? id.ToString() : string.Empty;
        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            $"Object '{target}' is of a type that could not be loaded, so it is kept as it was read and cannot be edited.",
            target,
            "Leave the object unchanged in the document, or remove it."));
    }

    // Whether the Uri the document gives a fallback, if any, still resolves to the file the fallback was read
    // from. Like any object, a fallback that the document gives no Uri keeps its file.
    private bool StaysInItsFile(IFallback fallback, JsonObject desired)
    {
        if (!desired.TryGetPropertyValue("Uri", out JsonNode? node))
        {
            return true;
        }

        if (fallback is not CoreObject { Uri: { } own }
            || node is not JsonValue value
            || !value.TryGetValue(out string? reference))
        {
            return false;
        }

        // The document writes the Uri relative to the base of the object around the fallback.
        CoreObject? owner = (fallback as IHierarchical)?.HierarchicalParent as CoreObject;
        Uri? baseUri = ResolveBaseUri(owner) ?? _documentBaseUri;
        try
        {
            return UriHelper.ResolvePersistedReference(reference, baseUri) == own;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // The node without what a document adds to an object's JSON: the list directives ($index, $after, $before
    // and $delete) an entry may carry, and the Uri of an object stored in a file of its own.
    private static JsonObject? WithoutMetadata(JsonObject? node)
    {
        if (node is null
            || !node.Any(static pair => pair.Key is "$index" or "$after" or "$before" or "$delete" or "Uri"))
        {
            return node;
        }

        var copy = (JsonObject)node.DeepClone();
        copy.Remove("$index");
        copy.Remove("$after");
        copy.Remove("$before");
        copy.Remove("$delete");
        copy.Remove("Uri");
        return copy;
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
            : scene.FrameSize.Value.Width;
        int height = desired.TryGetPropertyValue("Height", out JsonNode? heightNode)
            ? heightNode!.GetValue<int>()
            : scene.FrameSize.Value.Height;
        scene.FrameSize.Value = new PixelSize(width, height);

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
                    .Where(id => id != Guid.Empty && scene.Children.Any(element => element.Id.Value == id))
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
        // existing element's .belm outside the project.
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
        List<(CoreObject Value, JsonObject Desired)> changedValues = TakeChangedValuesOfTheSameObject(target, payload);
        CoreSerializer.PopulateFromJsonObject(target, target.GetType(), payload, CreateOptions(target));
        ClearAbsentRegisteredObjectProperties(target, desired, payload);
        ClearAbsentObjectProperties(target, desired, payload);
        foreach ((CoreObject value, JsonObject valueDesired) in changedValues)
        {
            ApplyCoreObject(value, valueDesired);
        }

        ApplyListProperties(target, desired);
        ApplyAnimations(target, desired);
        ApplyExpressions(target, desired);
    }

    // Takes the changed values of the same type and Id, to apply in place as the editor does and keep unknown members.
    private List<(CoreObject Value, JsonObject Desired)> TakeChangedValuesOfTheSameObject(
        EngineObject target, JsonObject payload)
    {
        var values = new List<(CoreObject, JsonObject)>();
        foreach (IProperty property in target.Properties)
        {
            // A member its type reads itself may be set even when left out of the payload, so the populate reads it.
            if (!CoreObject.IsAutoSerialized(property)
                || property.Value is not CoreObject value
                || value is IFallback or IEnumerable
                || payload[property.Name] is not JsonObject json
                || CoreSerializer.ResolveActualType(json, property.ValueType) != value.GetType()
                || !IdentityMatches(value, json)
                || JsonNode.DeepEquals(SerializeCurrent(value), json))
            {
                continue;
            }

            payload.Remove(property.Name);
            values.Add((value, json));
        }

        return values;
    }

    private void ApplyRegisteredProperties(CoreObject target, JsonObject desired, IReadOnlySet<string> excluded)
    {
        // Base fields are applied as the CoreProperty fields they replace were, a list item by item.
        foreach (IProperty field in SavedBaseFields(target))
        {
            if (excluded.Contains(field.Name) || !desired.TryGetPropertyValue(field.Name, out JsonNode? fieldNode))
            {
                continue;
            }

            // A list property, like Scene.Layers, is read as an array of its items.
            Type fieldType = field is IListProperty listField ? listField.ElementType.MakeArrayType() : field.ValueType;
            object? fieldValue = fieldNode is null
                ? null
                : EnumJsonValueNormalizer.Deserialize(fieldNode, fieldType, CreateOptions(target));
            if (field.Value is ICoreList list && fieldValue is IList items)
            {
                ReplaceItems(list, items);
            }
            else
            {
                field.Value = fieldValue;
            }
        }
    }

    private static void NormalizeRegisteredPropertyValues(CoreObject target, JsonObject payload)
    {
        foreach (IProperty field in target.Properties.Where(target.IsBaseField))
        {
            NormalizePropertyValue(payload, field.Name, field.ValueType);
        }
    }

    private static void NormalizeEnginePropertyValues(EngineObject target, JsonObject payload)
    {
        foreach (IProperty property in target.Properties.Where(property => !target.IsBaseField(property)))
        {
            NormalizePropertyValue(payload, property.Name, property.ValueType);
        }
    }

    // The base fields saved with target.
    private static IEnumerable<IProperty> SavedBaseFields(CoreObject target)
        => target.Properties.Where(property => target.IsBaseField(property) && CoreObject.IsAutoSerialized(property));

    // Replaces the items of a list a base field holds, as its CoreProperty accessor did with ICoreList<T>.Replace.
    private static void ReplaceItems(ICoreList list, IList items)
    {
        Type itemType = list.GetType().GetInterfaces()
            .First(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICoreList<>))
            .GenericTypeArguments[0];
        typeof(DeclarativeDocumentApplier)
            .GetMethod(nameof(ReplaceItemsOf), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(itemType)
            .Invoke(null, [list, items]);
    }

    private static void ReplaceItemsOf<T>(ICoreList<T> list, IList items) => list.Replace([.. items.Cast<T>()]);

    private static void NormalizePropertyValue(JsonObject payload, string propertyName, Type valueType)
    {
        if (payload.TryGetPropertyValue(propertyName, out JsonNode? valueNode) && valueNode is not null)
        {
            payload[propertyName] = EnumJsonValueNormalizer.Normalize(valueNode, valueType);
        }
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
                Mode = CoreSerializationMode.EmbedReferencedObjects,
                KeepUnknownMembers = false
            });
    }

    private static void ClearAbsentRegisteredObjectProperties(
        CoreObject target,
        JsonObject desired,
        JsonObject serializedPayload)
    {
        // Lists (e.g. Element.Objects, KeyFrames) are cleared by their specialized handlers, never by nulling, and an
        // unsaved field never appears in a document, so its absence carries no intent.
        foreach (IProperty field in SavedBaseFields(target))
        {
            if (field.ValueType.IsValueType
                || field.ValueType == typeof(string)
                || typeof(ICoreList).IsAssignableFrom(field.ValueType)
                || desired.ContainsKey(field.Name)
                || serializedPayload.ContainsKey(field.Name))
            {
                continue;
            }

            field.Value = null;
        }
    }

    private static void ClearAbsentObjectProperties(
        EngineObject target,
        JsonObject desired,
        JsonObject serializedPayload)
    {
        foreach (IProperty property in target.Properties.Where(property => !target.IsBaseField(property)))
        {
            if (property is IListProperty
                || property.ValueType.IsValueType
                || property.ValueType == typeof(string)
                || desired.ContainsKey(property.Name)
                || serializedPayload.ContainsKey(property.Name))
            {
                continue;
            }

            property.Value = null;
        }
    }

    private static JsonObject NormalizeCoreSerializableJson(JsonObject json, Type baseType)
    {
        JsonObject normalized = (JsonObject)EnumJsonValueNormalizer.Normalize(json, baseType);
        Type? actualType = CoreSerializer.ResolveActualType(normalized, baseType);
        if (actualType is null)
        {
            return normalized;
        }

        if (typeof(Element).IsAssignableFrom(actualType))
        {
            NormalizeIdentityArray(normalized, nameof(Element.Objects), typeof(EngineObject));
        }
        else if (typeof(IKeyFrameAnimation).IsAssignableFrom(actualType))
        {
            NormalizeIdentityArray(normalized, nameof(IKeyFrameAnimation.KeyFrames), typeof(IKeyFrame));
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

        string path = Path.Combine(sceneDirectory, $"{element.Id.Value:N}.{EditorConstants.ElementFileExtension}");
        for (int index = 1; File.Exists(path) || scene.Children.Any(item => item.Uri?.LocalPath == path); index++)
        {
            path = Path.Combine(sceneDirectory, $"{element.Id.Value:N}-{index}.{EditorConstants.ElementFileExtension}");
        }

        element.Uri = new Uri(path);
    }

    private static bool IdentityMatches(CoreObject current, JsonObject desired)
    {
        return !CollectionReconciler.TryGetId(desired, out Guid id) || current.Id.Value == id;
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

    // Applying a document keeps no member Beutl does not know. A document leaves out the ones an object kept from
    // its file, and populating leaves those on the object; a key the agent adds that Beutl does not know is
    // dropped, as it was before objects kept unknown members.
    internal static CoreSerializerOptions CreateOptions(Uri? baseUri)
    {
        return new CoreSerializerOptions
        {
            BaseUri = baseUri,
            Mode = CoreSerializationMode.Read | CoreSerializationMode.EmbedReferencedObjects,
            PreserveUnchangedValues = true,
            KeepUnknownMembers = false
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
