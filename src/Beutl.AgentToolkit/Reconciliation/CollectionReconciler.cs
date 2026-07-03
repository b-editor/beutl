using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Reconciliation;

public static class CollectionReconciler
{
    private const string StaleHandleHint =
        "Omit Id to create a new object. When adding Objects to an existing Element, keep the parent Element Id and omit Id on each new Object. To update an existing object, call read_document, then retry apply_edit with an existing Id.";

    public static bool IsIdentityArray(JsonArray array)
    {
        return array.OfType<JsonObject>().Any(item => item.ContainsKey(nameof(CoreObject.Id)));
    }

    public static HashSet<Guid> MintMissingIds(JsonNode? node, IReadOnlySet<Guid>? reservedIds = null)
    {
        var minted = new HashSet<Guid>();
        // A colliding mint lands in newIds, bypasses the stale-handle check, and
        // persists a second object with the same id, corrupting the document.
        var reserved = reservedIds is null ? new HashSet<Guid>() : new HashSet<Guid>(reservedIds);
        foreach ((Guid id, _) in EnumerateIds(node))
        {
            reserved.Add(id);
        }

        MintMissingIds(node, minted, reserved, "$");
        return minted;
    }

    private static void MintMissingIds(JsonNode? node, HashSet<Guid> minted, HashSet<Guid> reserved, string path)
    {
        if (node is JsonObject obj)
        {
            bool isNewSubtree = false;
            if (obj.ContainsKey("$type") && !obj.ContainsKey(nameof(CoreObject.Id)))
            {
                Guid id = CreateDeterministicId(path, obj);
                for (int salt = 2; reserved.Contains(id); salt++)
                {
                    id = CreateDeterministicId($"{path}#{salt}", obj);
                }

                obj[nameof(CoreObject.Id)] = id.ToString();
                minted.Add(id);
                reserved.Add(id);
                isNewSubtree = true;
            }

            foreach (KeyValuePair<string, JsonNode?> pair in obj.ToArray())
            {
                MintMissingIds(pair.Value, minted, reserved, $"{path}/{pair.Key}");
            }

            if (isNewSubtree)
            {
                foreach ((Guid id, _) in EnumerateIds(obj))
                {
                    minted.Add(id);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                MintMissingIds(array[i], minted, reserved, $"{path}[{i}]");
            }
        }
    }

    public static HashSet<Guid> CollectIds(JsonNode? node)
    {
        var ids = new HashSet<Guid>();
        foreach ((Guid id, _) in EnumerateIds(node))
        {
            ids.Add(id);
        }

        return ids;
    }

    public static HashSet<Guid> CollectDuplicatedIds(JsonNode? node)
    {
        var seen = new HashSet<Guid>();
        var duplicated = new HashSet<Guid>();
        foreach ((Guid id, _) in EnumerateIds(node))
        {
            if (!seen.Add(id))
            {
                duplicated.Add(id);
            }
        }

        return duplicated;
    }

    public static ToolError? ValidateNoDuplicateIdsInIdentityArrays(
        JsonNode? node,
        IReadOnlySet<Guid>? toleratedIds = null)
        => ValidateNoDuplicateIdsInIdentityArrays(node, toleratedIds, "$");

    private static ToolError? ValidateNoDuplicateIdsInIdentityArrays(
        JsonNode? node,
        IReadOnlySet<Guid>? toleratedIds,
        string path)
    {
        if (node is JsonObject obj)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in obj.ToArray())
            {
                if (ValidateNoDuplicateIdsInIdentityArrays(pair.Value, toleratedIds, $"{path}/{pair.Key}") is { } error)
                {
                    return error;
                }
            }
        }
        else if (node is JsonArray array)
        {
            var seen = new HashSet<Guid>();
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonObject item
                    && TryGetId(item, out Guid id)
                    && !seen.Add(id)
                    && toleratedIds?.Contains(id) != true)
                {
                    return new ToolError(
                        ErrorCode.ValidationRejected,
                        $"Array '{path}' contains Id '{id}' more than once.",
                        id.ToString(),
                        "Each Id may appear at most once per array. Remove the duplicate entry, or omit Id so a fresh object is created.");
                }

                if (ValidateNoDuplicateIdsInIdentityArrays(array[i], toleratedIds, $"{path}[{i}]") is { } error)
                {
                    return error;
                }
            }
        }

        return null;
    }

    internal static Guid CreateDeterministicId(string path, JsonObject obj)
    {
        string discriminator = obj.TryGetPropertyValue("$type", out JsonNode? typeNode)
            ? typeNode?.GetValue<string>() ?? string.Empty
            : string.Empty;
        string payload = $"{path}\n{discriminator}\n{obj.ToJsonString()}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }

    public static ToolError? ValidateIdentityReferences(JsonObject current, JsonObject desired, ISet<Guid> newIds)
    {
        Dictionary<Guid, string?> currentTypes = CollectTypesById(current);
        foreach ((Guid id, string? type) in EnumerateIds(desired))
        {
            if (newIds.Contains(id))
            {
                continue;
            }

            if (!currentTypes.TryGetValue(id, out string? currentType))
            {
                return new ToolError(
                    ErrorCode.StaleHandle,
                    $"No entity with Id '{id}' exists in the current session.",
                    id.ToString(),
                    StaleHandleHint);
            }

            if (type is not null && currentType is not null && type != currentType)
            {
                return new ToolError(
                    ErrorCode.ValidationRejected,
                    $"Entity '{id}' cannot change type from '{currentType}' to '{type}'.",
                    id.ToString(),
                    "Delete the old entity and insert a new one without reusing the Id.");
            }
        }

        return null;
    }

    internal static HashSet<Guid> CollectInsertedIds(JsonObject current, JsonObject desired)
    {
        var ids = new HashSet<Guid>();
        CollectInsertedIds(current, desired, ids);
        return ids;
    }

    private static void CollectInsertedIds(JsonNode? current, JsonNode? desired, HashSet<Guid> inserted)
    {
        if (current is JsonArray currentArray && desired is JsonArray desiredArray)
        {
            if (IsIdentityArray(currentArray) || IsIdentityArray(desiredArray))
            {
                // First-wins: documents corrupted with duplicate ids must stay editable.
                var currentById = new Dictionary<Guid, JsonObject>();
                foreach (JsonObject item in currentArray.OfType<JsonObject>())
                {
                    if (TryGetId(item, out Guid itemId))
                    {
                        currentById.TryAdd(itemId, item);
                    }
                }

                foreach (JsonObject desiredItem in desiredArray.OfType<JsonObject>())
                {
                    if (!TryGetId(desiredItem, out Guid id))
                    {
                        continue;
                    }

                    if (!currentById.TryGetValue(id, out JsonObject? currentItem))
                    {
                        foreach ((Guid insertedId, _) in EnumerateIds(desiredItem))
                        {
                            inserted.Add(insertedId);
                        }
                    }
                    else
                    {
                        CollectInsertedIds(currentItem, desiredItem, inserted);
                    }
                }

                return;
            }

            int count = Math.Min(currentArray.Count, desiredArray.Count);
            for (int i = 0; i < count; i++)
            {
                CollectInsertedIds(currentArray[i], desiredArray[i], inserted);
            }

            return;
        }

        if (current is JsonObject currentObject && desired is JsonObject desiredObject)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in desiredObject.ToArray())
            {
                currentObject.TryGetPropertyValue(pair.Key, out JsonNode? currentChild);
                CollectInsertedIds(currentChild, pair.Value, inserted);
            }
        }
    }

    private static Dictionary<Guid, string?> CollectTypesById(JsonNode? node)
    {
        var result = new Dictionary<Guid, string?>();
        foreach ((Guid id, string? type) in EnumerateIds(node))
        {
            result[id] = type;
        }

        return result;
    }

    private static IEnumerable<(Guid Id, string? Type)> EnumerateIds(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (TryGetId(obj, out Guid id))
            {
                string? type = obj.TryGetPropertyValue("$type", out JsonNode? typeNode)
                    ? typeNode?.GetValue<string>()
                    : null;
                yield return (id, type);
            }

            foreach (JsonNode? child in obj.Select(pair => pair.Value).ToArray())
            {
                foreach ((Guid childId, string? childType) in EnumerateIds(child))
                {
                    yield return (childId, childType);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array.ToArray())
            {
                foreach ((Guid childId, string? childType) in EnumerateIds(child))
                {
                    yield return (childId, childType);
                }
            }
        }
    }

    internal static bool TryGetId(JsonObject obj, out Guid id)
    {
        id = default;
        return obj.TryGetPropertyValue(nameof(CoreObject.Id), out JsonNode? idNode)
               && idNode?.GetValue<string>() is { } idText
               && Guid.TryParse(idText, out id);
    }

    private static Guid ReadId(JsonObject obj)
    {
        TryGetId(obj, out Guid id);
        return id;
    }
}
