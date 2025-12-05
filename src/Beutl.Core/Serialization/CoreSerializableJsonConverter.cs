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
            if (!Uri.TryCreate(uriString, UriKind.RelativeOrAbsolute, out Uri? uri))
            {
                throw new JsonException($"Invalid URI: {uriString}");
            }

            if (!uri.IsAbsoluteUri)
            {
                if (parentContext == null)
                    throw new JsonException("Cannot resolve relative URI without a parent context.");

                if (!Uri.TryCreate(parentContext.BaseUri, uriString, out uri))
                {
                    throw new JsonException($"Invalid relative URI: {uriString}");
                }
            }

            return CoreSerializer.RestoreFromUri(uri, typeToConvert) as ICoreSerializable;
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, ICoreSerializable value, JsonSerializerOptions options)
    {
        var parentContext = ThreadLocalSerializationContext.Current;
        if (value is CoreObject { Uri: not null } coreObj && parentContext != null)
        {
            if (parentContext.Mode.HasFlag(CoreSerializationMode.SaveReferencedObjects))
            {
                var node = CoreSerializer.SerializeToJsonObject(value,
                    new CoreSerializerOptions { BaseUri = coreObj.Uri });

                var path = Uri.UnescapeDataString(coreObj.Uri.LocalPath);
                using var stream = File.Create(path);
                using var innerWriter = new Utf8JsonWriter(stream, JsonHelper.WriterOptions);
                node.WriteTo(innerWriter);
            }

            var serializedUri = coreObj.Uri;
            if (parentContext.BaseUri?.Scheme == coreObj.Uri.Scheme)
            {
                serializedUri = parentContext.BaseUri.MakeRelativeUri(coreObj.Uri);
            }

            if (parentContext.Mode.HasFlag(CoreSerializationMode.EmbedReferencedObjects))
            {
                var node = CoreSerializer.SerializeToJsonObject(value,
                    new CoreSerializerOptions { BaseUri = coreObj.Uri });

                node["Uri"] = serializedUri.ToString();
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
