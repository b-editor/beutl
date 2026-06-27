using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Reconciliation;

public static class CollectionReconciler
{
    public static bool IsIdentityArray(JsonArray array)
    {
        return array.OfType<JsonObject>().Any(item => item.ContainsKey(nameof(CoreObject.Id)));
    }

    public static HashSet<Guid> MintMissingIds(JsonNode? node)
    {
        var minted = new HashSet<Guid>();
        MintMissingIds(node, minted, "$");
        return minted;
    }

    private static void MintMissingIds(JsonNode? node, HashSet<Guid> minted, string path)
    {
        if (node is JsonObject obj)
        {
            bool isNewSubtree = false;
            if (obj.ContainsKey("$type") && !obj.ContainsKey(nameof(CoreObject.Id)))
            {
                Guid id = CreateDeterministicId(path, obj);
                obj[nameof(CoreObject.Id)] = id.ToString();
                minted.Add(id);
                isNewSubtree = true;
            }

            foreach (KeyValuePair<string, JsonNode?> pair in obj.ToArray())
            {
                MintMissingIds(pair.Value, minted, $"{path}/{pair.Key}");
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
                MintMissingIds(array[i], minted, $"{path}[{i}]");
            }
        }
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
                return new ToolError(ErrorCode.StaleHandle, $"No entity with Id '{id}' exists in the current session.", id.ToString());
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
}
