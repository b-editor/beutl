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

            using Stream stream = UriHelper.ResolveStream(uri);

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
            var node = CoreSerializer.SerializeToJsonObject(value, new CoreSerializerOptions { BaseUri = coreObj.Uri });
            if (!coreObj.Uri.IsFile)
            {
                var jsonString = node.ToJsonString(new() { WriteIndented = false });
                var uri = UriHelper.CreateBase64DataUri("application/json",
                    System.Text.Encoding.UTF8.GetBytes(jsonString));
                writer.WriteStringValue(uri.ToString());
            }
            else
            {
                var serializedUri = coreObj.Uri;
                if (parentContext.BaseUri?.Scheme == coreObj.Uri.Scheme)
                {
                    // Trailing slashによって動作が変わってしまう
                    serializedUri = parentContext.BaseUri.MakeRelativeUri(coreObj.Uri);
                }

                writer.WriteStringValue(serializedUri.ToString());

                using var stream = File.Create(coreObj.Uri.LocalPath);
                using var innerWriter = new Utf8JsonWriter(stream);
                node.WriteTo(innerWriter);
            }

            return;
        }

        JsonObject obj = CoreSerializer.SerializeToJsonObject(value);
        obj.WriteTo(writer, options);
    }
}
