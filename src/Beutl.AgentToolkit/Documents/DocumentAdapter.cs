using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Documents;

public sealed class DocumentAdapter
{
    private readonly Dictionary<Guid, JsonObject> _unknownContent = [];

    public JsonObject Read(ICoreSerializable root)
    {
        ArgumentNullException.ThrowIfNull(root);

        JsonObject document = CoreSerializer.SerializeToJsonObject(root, CreateOptions(root, CoreSerializationMode.EmbedReferencedObjects));
        RestoreUnknownContent(document);
        SchemaVersion.Stamp(document);
        return document;
    }

    public void Write(ICoreSerializable root, JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(document);

        SchemaVersion.EnsureKnown(document);
        JsonObject current = Read(root);
        CaptureUnknownContent(document, current);

        JsonObject payload = (JsonObject)document.DeepClone();
        payload.Remove(SchemaVersion.PropertyName);
        CoreSerializer.PopulateFromJsonObject(root, root.GetType(), payload, CreateOptions(root, CoreSerializationMode.Read | CoreSerializationMode.EmbedReferencedObjects));
    }

    private static CoreSerializerOptions CreateOptions(ICoreSerializable root, CoreSerializationMode mode)
    {
        return new CoreSerializerOptions
        {
            BaseUri = root is CoreObject coreObject ? coreObject.Uri : null,
            Mode = mode
        };
    }

    private void CaptureUnknownContent(JsonObject submitted, JsonObject current)
    {
        if (TryGetId(submitted, out Guid id))
        {
            var extras = new JsonObject();
            foreach (KeyValuePair<string, JsonNode?> pair in submitted)
            {
                if (pair.Key == SchemaVersion.PropertyName)
                {
                    continue;
                }

                if (!current.ContainsKey(pair.Key))
                {
                    extras[pair.Key] = pair.Value?.DeepClone();
                }
            }

            if (extras.Count > 0)
            {
                _unknownContent[id] = extras;
            }
            else
            {
                _unknownContent.Remove(id);
            }
        }

        foreach (KeyValuePair<string, JsonNode?> pair in submitted)
        {
            if (!current.TryGetPropertyValue(pair.Key, out JsonNode? currentNode))
            {
                continue;
            }

            CaptureUnknownContent(pair.Value, currentNode);
        }
    }

    private void CaptureUnknownContent(JsonNode? submitted, JsonNode? current)
    {
        if (submitted is JsonObject submittedObj && current is JsonObject currentObj)
        {
            CaptureUnknownContent(submittedObj, currentObj);
        }
        else if (submitted is JsonArray submittedArray && current is JsonArray currentArray)
        {
            foreach (JsonObject submittedItem in submittedArray.OfType<JsonObject>())
            {
                if (!TryGetId(submittedItem, out Guid id))
                {
                    continue;
                }

                JsonObject? currentItem = currentArray
                    .OfType<JsonObject>()
                    .FirstOrDefault(item => TryGetId(item, out Guid currentId) && currentId == id);
                if (currentItem is not null)
                {
                    CaptureUnknownContent(submittedItem, currentItem);
                }
            }
        }
    }

    private void RestoreUnknownContent(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (TryGetId(obj, out Guid id) && _unknownContent.TryGetValue(id, out JsonObject? extras))
            {
                foreach (KeyValuePair<string, JsonNode?> pair in extras)
                {
                    if (!obj.ContainsKey(pair.Key))
                    {
                        obj[pair.Key] = pair.Value?.DeepClone();
                    }
                }
            }

            foreach (JsonNode? child in obj.Select(pair => pair.Value).ToArray())
            {
                RestoreUnknownContent(child);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array.ToArray())
            {
                RestoreUnknownContent(child);
            }
        }
    }

    private static bool TryGetId(JsonObject obj, out Guid id)
    {
        id = default;
        return obj.TryGetPropertyValue(nameof(CoreObject.Id), out JsonNode? idNode)
               && idNode?.GetValue<string>() is { } idText
               && Guid.TryParse(idText, out id);
    }
}
