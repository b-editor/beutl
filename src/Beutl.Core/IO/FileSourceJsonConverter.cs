using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.Serialization;

namespace Beutl.IO;

public class FileSourceJsonConverter : JsonConverter<IFileSource>
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(IFileSource));
    }

    public override IFileSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonNode = JsonNode.Parse(ref reader);
        var parentContext = ThreadLocalSerializationContext.Current;
        if (jsonNode is JsonValue jsonValue && jsonValue.TryGetValue(out string? uriString))
        {
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

            IFileSource? instance = typeToConvert == typeof(IFileSource)
                ? new BlobFileSource()
                : Activator.CreateInstance(typeToConvert) as IFileSource;
            if (instance == null)
            {
                throw new JsonException($"Could not create instance of type {typeToConvert.FullName}.");
            }

            if (uri.Scheme == "data")
            {
                // Data URIスキームの処理
                var (data, _) = DataUriHelper.ParseDataUri(uri);
                instance.ReadFrom(new MemoryStream(data), uri);
                return instance;
            }

            if (parentContext == null) throw new JsonException("Cannot resolve URI without a parent context.");

            var stream = parentContext.FileSystem.OpenFile(uri);
            instance.ReadFrom(stream, uri);
            return instance;
        }
        else
        {
            throw new JsonException();
        }
    }

    public override void Write(Utf8JsonWriter writer, IFileSource value, JsonSerializerOptions options)
    {
        var parentContext = ThreadLocalSerializationContext.Current;
        if (parentContext == null) throw new JsonException("Cannot serialize IFileSource without a parent context.");

        var serializedUri = value.Uri;
        if (parentContext.BaseUri?.Scheme == value.Uri.Scheme)
        {
            serializedUri = parentContext.BaseUri.MakeRelativeUri(value.Uri);
        }

        writer.WriteStringValue(serializedUri.ToString());

        // ファイルに書き込むかの判断はここで行う
        if (!parentContext.Mode.HasFlag(CoreSerializationMode.WriteBlobFiles) && value.IsBlob)
        {
            return;
        }

        using var stream = parentContext.FileSystem.CreateFile(value.Uri);
        value.WriteTo(stream);
    }
}
