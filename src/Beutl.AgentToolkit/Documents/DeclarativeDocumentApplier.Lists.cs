using System.Collections;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Collections;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Documents;

internal sealed partial class DeclarativeDocumentApplier
{
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

    private void ApplyIdentityList(IList list, Type elementBaseType, string fieldName, JsonArray desired, CoreObject? owner)
    {
        if (!CollectionReconciler.IsIdentityArray(desired))
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

            CoreObject item = ApplyOrCreateListItem(
                list,
                elementBaseType,
                itemJson,
                CreateIdentityListItemPath(fieldName, desiredIndex),
                owner);
            desiredIds.Add(item.Id);
            PlaceListItem(list, item, desiredIndex, owner);
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

    private CoreObject ApplyOrCreateListItem(
        IList list,
        Type elementBaseType,
        JsonObject itemJson,
        string itemPath,
        CoreObject? owner)
    {
        CoreObject item;
        try
        {
            if (CollectionReconciler.TryGetId(itemJson, out Guid id) && FindById(list, id) is { } existing)
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
                    Uri? incomingBaseUri = ResolveIncomingBaseUri(scene.Uri, itemJson);
                    ApplyDetached(element, elementJson, incomingBaseUri);
                    // Flow operators have a leading PortalObject; use the actual content type for its color.
                    if (!elementJson.ContainsKey(nameof(Element.AccentColor))
                        && element.Objects.FirstOrDefault(obj => obj is not PortalObject) is { } content)
                    {
                        Type contentType = content.GetType();
                        element.AccentColor = ElementAccentColorGenerator.GenerateColor(contentType.FullName ?? contentType.Name);
                    }

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

        return item;
    }

    private static void PlaceListItem(IList list, CoreObject item, int desiredIndex, CoreObject? owner)
    {
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
                ? DeserializeListItem(obj, elementBaseType, ResolveBaseUri(owner) ?? _documentBaseUri)
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

    // An embedded object that carries its own "Uri" is the base for its own subtree. CoreSerializer
    // resolves that field but leaves BaseUri alone when options are already non-null (ReflectUri's
    // `options ??=`), so the object's Uri is absolutized here and the resolved value passed as the
    // base — absolutizing keeps ReflectUri from re-resolving it against itself.
    private object DeserializeListItem(JsonObject itemJson, Type elementBaseType, Uri? ownerBaseUri)
    {
        JsonObject normalized = NormalizeCoreSerializableJson(itemJson, elementBaseType);
        if (ResolveIncomingBaseUri(ownerBaseUri, itemJson) is { } ownUri)
        {
            normalized["Uri"] = ownUri.ToString();
            return CoreSerializer.DeserializeFromJsonObject(normalized, elementBaseType, CreateOptions(ownUri));
        }

        return CoreSerializer.DeserializeFromJsonObject(normalized, elementBaseType, CreateOptions(ownerBaseUri));
    }
}
