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

            Stream? stream;
            if (uri.Scheme == "data")
            {
                // Data URIスキームの処理
                var (data, _) = DataUriHelper.ParseDataUri(uri);
                stream = new MemoryStream(data);
            }
            else
            {
                if (parentContext == null) throw new JsonException("Cannot resolve URI without a parent context.");

                stream = parentContext.FileSystem.OpenFile(uri);
            }

            var node = JsonNode.Parse(stream);
            if (node is JsonObject jsonObject1)
            {
                var obj = CoreSerializer.DeserializeFromJsonObject(
                    jsonObject1, typeToConvert, new CoreSerializerOptions { BaseUri = uri });

                if (obj is CoreObject coreObj)
                {
                    coreObj.Uri = uri;
                }

                return obj as ICoreSerializable;
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, ICoreSerializable value, JsonSerializerOptions options)
    {
        var parentContext = ThreadLocalSerializationContext.Current;
        if (value is CoreObject { Uri: not null } coreObj && parentContext != null)
        {
            var serializedUri = coreObj.Uri;
            if (parentContext.BaseUri?.Scheme == coreObj.Uri.Scheme)
            {
                // Trailing slashによって動作が変わってしまう
                serializedUri = parentContext.BaseUri.MakeRelativeUri(coreObj.Uri);
            }

            writer.WriteStringValue(serializedUri.ToString());

            using var stream = parentContext.FileSystem.CreateFile(coreObj.Uri);
            using var innerWriter = new Utf8JsonWriter(stream);
            var node = CoreSerializer.SerializeToJsonObject(value, new CoreSerializerOptions { BaseUri = coreObj.Uri });
            node.WriteTo(innerWriter);
            return;
        }

        JsonObject obj = CoreSerializer.SerializeToJsonObject(value);
        obj.WriteTo(writer, options);
    }
}
