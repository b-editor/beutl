using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Beutl.Serialization;

public sealed class CoreSerializableJsonConverter : JsonConverter<ICoreSerializable>
{
    public override ICoreSerializable? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonNode = JsonNode.Parse(ref reader);
        if (jsonNode is JsonObject jsonObject)
        {
            return CoreSerializer.DeserializeFromJsonObject(jsonObject, typeToConvert) as ICoreSerializable;
        }
        else if (jsonNode is JsonValue jsonValue && jsonValue.TryGetValue(out string? uriString))
        {
            var parentContext = ThreadLocalSerializationContext.Current;
            Uri uri = UriHelper.ResolvePersistedReference(uriString, parentContext?.BaseUri);

            return CoreSerializer.RestoreFromUri(uri, typeToConvert) as ICoreSerializable;
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, ICoreSerializable value, JsonSerializerOptions options)
    {
        var parentContext = ThreadLocalSerializationContext.Current;
        if (value is CoreObject { Uri: not null } coreObj && parentContext != null)
        {
            // Hand the requirement over before the file is written, as SerializeCoreSerializable
            // does: this branch returns without ever reaching SerializeToJsonObject.
            JsonSerializationContext.TransferRetainedMigration(value, parentContext);

            if (parentContext.Mode.HasFlag(CoreSerializationMode.SaveReferencedObjects))
            {
                CoreSerializer.StoreToUri(value, coreObj.Uri,
                    CoreSerializationMode.Write | CoreSerializationMode.SaveReferencedObjects);
            }

            var serializedUri = coreObj.Uri;
            if (parentContext.BaseUri?.Scheme == coreObj.Uri.Scheme)
            {
                serializedUri = parentContext.BaseUri.MakeRelativeUri(coreObj.Uri);
            }

            if (parentContext.Mode.HasFlag(CoreSerializationMode.EmbedReferencedObjects)
                && CoreSerializer.SerializeEmbeddedReference(coreObj, serializedUri) is { } node)
            {
                node.WriteTo(writer, options);
            }
            else
            {
                writer.WriteStringValue(serializedUri.ToString());
            }

            return;
        }

        JsonObject obj = CoreSerializer.SerializeToJsonObject(value);
        obj.WriteTo(writer, options);
    }
}
