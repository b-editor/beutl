using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;

namespace Beutl.AgentToolkit.MergePatch;

public static class MergePatch
{
    private const string StaleHandleHint =
        "Omit Id to create a new object. To update an existing object, call read_document, then retry apply_edit with an existing Id.";

    private static readonly string[] s_directives = ["$index", "$after", "$before"];

    public static JsonNode? Apply(JsonNode? target, JsonNode? patch)
    {
        return Apply(target, patch, "$");
    }

    private static JsonNode? Apply(JsonNode? target, JsonNode? patch, string path)
    {
        if (patch is JsonObject patchObject)
        {
            JsonObject? targetObject = target as JsonObject;
            if (ShouldReplaceTypedObject(targetObject, patchObject))
            {
                return patchObject.DeepClone();
            }

            JsonObject result = (JsonObject)(targetObject ?? []).DeepClone();

            foreach (KeyValuePair<string, JsonNode?> pair in patchObject)
            {
                if (pair.Value is null)
                {
                    result.Remove(pair.Key);
                }
                else
                {
                    result[pair.Key] = Apply(
                        result.TryGetPropertyValue(pair.Key, out JsonNode? current) ? current : null,
                        pair.Value,
                        $"{path}/{pair.Key}")?.DeepClone();
                }
            }

            return result;
        }

        if (patch is JsonArray patchArray)
        {
            if (ShouldUseIdentityMerge(target as JsonArray, patchArray))
            {
                return ApplyIdentityArray(target as JsonArray ?? [], patchArray, path);
            }

            return patch.DeepClone();
        }

        return patch?.DeepClone();
    }

    private static bool ShouldReplaceTypedObject(JsonObject? target, JsonObject patch)
    {
        if (target is null || TryGetId(patch, out _))
        {
            return false;
        }

        string? targetType = target.TryGetPropertyValue("$type", out JsonNode? targetTypeNode)
            ? targetTypeNode?.GetValue<string>()
            : null;
        string? patchType = patch.TryGetPropertyValue("$type", out JsonNode? patchTypeNode)
            ? patchTypeNode?.GetValue<string>()
            : null;

        return targetType is not null
               && patchType is not null
               && !string.Equals(targetType, patchType, StringComparison.Ordinal);
    }

    private static JsonArray ApplyIdentityArray(JsonArray target, JsonArray patch, string path)
    {
        JsonArray result = (JsonArray)target.DeepClone();

        int patchIndex = 0;
        foreach (JsonObject patchItem in patch.OfType<JsonObject>())
        {
            ValidateDirectives(patchItem);

            bool delete = patchItem.TryGetPropertyValue("$delete", out JsonNode? deleteNode)
                          && deleteNode?.GetValue<bool>() == true;
            bool hasId = TryGetId(patchItem, out Guid id);
            int currentIndex = hasId ? IndexOf(result, id) : -1;

            if (delete)
            {
                if (currentIndex >= 0)
                {
                    result.RemoveAt(currentIndex);
                }

                patchIndex++;
                continue;
            }

            JsonObject nextItem;
            if (!hasId)
            {
                id = CollectionReconciler.CreateDeterministicId($"{path}[new:{patchIndex}]", patchItem);
                nextItem = (JsonObject)patchItem.DeepClone();
                nextItem[nameof(CoreObject.Id)] = id.ToString();
                RemoveDirectives(nextItem);
                result.Add(nextItem);
                MoveWithDirectives(result, id, patchItem);
                patchIndex++;
                continue;
            }

            if (currentIndex < 0)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.StaleHandle,
                    $"No array member with Id '{id}' exists.",
                    id.ToString(),
                    StaleHandleHint));
            }

            JsonObject currentItem = (JsonObject)result[currentIndex]!;
            ValidateTypeMatch(currentItem, patchItem, id);
            nextItem = (JsonObject)Apply(currentItem, patchItem, $"{path}[Id={id}]")!;
            RemoveDirectives(nextItem);
            result[currentIndex] = nextItem;
            MoveWithDirectives(result, id, patchItem);
            patchIndex++;
        }

        return result;
    }

    private static bool ShouldUseIdentityMerge(JsonArray? target, JsonArray patch)
    {
        static bool HasIdentitySyntax(JsonArray array)
        {
            return array.OfType<JsonObject>().Any(item =>
                item.ContainsKey(nameof(CoreObject.Id))
                || item.ContainsKey("$delete")
                || s_directives.Any(item.ContainsKey));
        }

        return target is not null && HasIdentitySyntax(target) || HasIdentitySyntax(patch);
    }

    private static void ValidateDirectives(JsonObject item)
    {
        int count = s_directives.Count(item.ContainsKey);
        if (count > 1)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                "Only one of $index, $after, or $before may be supplied on an array member."));
        }
    }

    private static void ValidateTypeMatch(JsonObject current, JsonObject patch, Guid id)
    {
        string? currentType = current.TryGetPropertyValue("$type", out JsonNode? currentTypeNode)
            ? currentTypeNode?.GetValue<string>()
            : null;
        string? patchType = patch.TryGetPropertyValue("$type", out JsonNode? patchTypeNode)
            ? patchTypeNode?.GetValue<string>()
            : null;

        if (currentType is not null && patchType is not null && currentType != patchType)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"Array member '{id}' cannot change type from '{currentType}' to '{patchType}'.",
                id.ToString()));
        }
    }

    private static void MoveWithDirectives(JsonArray array, Guid id, JsonObject directiveSource)
    {
        int sourceIndex = IndexOf(array, id);
        if (sourceIndex < 0)
        {
            return;
        }

        int? targetIndex = null;
        if (directiveSource.TryGetPropertyValue("$index", out JsonNode? indexNode))
        {
            targetIndex = Math.Clamp(indexNode!.GetValue<int>(), 0, array.Count - 1);
        }
        else if (directiveSource.TryGetPropertyValue("$after", out JsonNode? afterNode))
        {
            Guid sibling = ReadGuid(afterNode, "$after");
            int siblingIndex = IndexOf(array, sibling);
            if (siblingIndex < 0)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.StaleHandle,
                    $"Sibling '{sibling}' was not found.",
                    sibling.ToString(),
                    StaleHandleHint));
            }

            targetIndex = siblingIndex + (siblingIndex < sourceIndex ? 1 : 0);
        }
        else if (directiveSource.TryGetPropertyValue("$before", out JsonNode? beforeNode))
        {
            Guid sibling = ReadGuid(beforeNode, "$before");
            int siblingIndex = IndexOf(array, sibling);
            if (siblingIndex < 0)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.StaleHandle,
                    $"Sibling '{sibling}' was not found.",
                    sibling.ToString(),
                    StaleHandleHint));
            }

            targetIndex = siblingIndex > sourceIndex ? siblingIndex - 1 : siblingIndex;
        }

        if (targetIndex is null || targetIndex == sourceIndex)
        {
            return;
        }

        JsonNode? item = array[sourceIndex];
        array.RemoveAt(sourceIndex);
        array.Insert(Math.Clamp(targetIndex.Value, 0, array.Count), item);
    }

    private static int IndexOf(JsonArray array, Guid id)
    {
        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is JsonObject obj && TryGetId(obj, out Guid current) && current == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryGetId(JsonObject obj, out Guid id)
    {
        id = default;
        return obj.TryGetPropertyValue(nameof(CoreObject.Id), out JsonNode? node)
               && node?.GetValue<string>() is { } text
               && Guid.TryParse(text, out id);
    }

    private static Guid ReadGuid(JsonNode? node, string name)
    {
        return node?.GetValue<string>() is { } text && Guid.TryParse(text, out Guid id)
            ? id
            : throw new ReconcileException(new ToolError(ErrorCode.ValidationRejected, $"{name} must be a Guid string."));
    }

    private static void RemoveDirectives(JsonObject obj)
    {
        obj.Remove("$delete");
        foreach (string directive in s_directives)
        {
            obj.Remove(directive);
        }
    }
}
