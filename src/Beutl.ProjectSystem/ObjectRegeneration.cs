using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Graphics3D.Models;
using Beutl.Media.Source;
using Beutl.Serialization;
using Beutl.Utilities;

namespace Beutl.ProjectSystem;

internal static class ObjectRegeneration
{
    private const int GuidLength = 36;
    private const int BufferSizeDefault = 16 * 1024;

    internal static object? CopyOwnedValue(object? value)
    {
        if (value is not CoreObject source || IsReferencedProjectItem(source))
            return value;

        var copy = CreateCopy(source, source.GetType());
        // A file source's URI is serialized by its property converter, so restore it when copying the source itself.
        if (source is MediaSource { HasUri: true } media)
            ((MediaSource)copy).ReadFrom(media.Uri);
        else if (source is ModelSource { HasUri: true } model)
            ((ModelSource)copy).RelinkFrom(model);

        RenewCopiedValues(copy);
        return copy;
    }

    internal static ICoreSerializable Copy(ICoreSerializable obj, Type actualType)
    {
        var copy = CreateCopy(obj, actualType);
        RenewCopiedValues(copy);
        return copy;
    }

    internal static string ToJson(ICoreSerializable obj)
    {
        using var output = new PooledArrayBufferWriter<byte>(BufferSizeDefault);
        Write(obj, output);
        return Encoding.UTF8.GetString(GetWrittenSpan(output));
    }

    private static ICoreSerializable CreateCopy(ICoreSerializable obj, Type actualType)
    {
        using var output = new PooledArrayBufferWriter<byte>(BufferSizeDefault);
        Write(obj, output);
        JsonObject json = JsonNode.Parse(GetWrittenSpan(output))!.AsObject();
        var copy = (ICoreSerializable)Activator.CreateInstance(actualType)!;
        CoreSerializer.PopulateFromJsonObject(copy, actualType, json);
        return copy;
    }

    private static void Write(ICoreSerializable obj, PooledArrayBufferWriter<byte> output)
    {
        // The copy holds its file-backed objects itself, so their Ids are in the JSON; a scene stays a reference.
        JsonObject jsonObject = CoreSerializer.SerializeToJsonObject(obj, new CoreSerializerOptions
        {
            Mode = CoreSerializationMode.EmbedReferencedObjects,
            EmbedsCopy = true
        });

        var referenceMembers = new Dictionary<Guid, HashSet<string>>();
        foreach (CoreObject owner in ChildObjects.Walk(obj, IsReferencedProjectItem).OfType<CoreObject>())
        {
            HashSet<string> names = [.. owner.RegisteredProperties
                .OfType<IExpressionProperty>().Where(p => p.IsReference).Select(p => p.Name)];
            if (names.Count > 0)
                referenceMembers[owner.Id.Value] = names;
        }

        // Inline reference values keep their format, but only owned objects contribute Ids to renew.
        var newIds = new Dictionary<Guid, Guid>();
        foreach (Guid id in CollectSerializedIds(jsonObject, referenceMembers))
            newIds.TryAdd(id, Guid.NewGuid());

        JsonSerializerOptions options = JsonHelper.SerializerOptions;
        var writerOptions = new JsonWriterOptions
        {
            Encoder = options.Encoder,
            Indented = options.WriteIndented,
            MaxDepth = options.MaxDepth
        };

        using (var writer = new Utf8JsonWriter(output, writerOptions))
        {
            jsonObject.WriteTo(writer, options);
        }

        ReplaceIds(GetWrittenSpan(output), newIds);
    }

    // One pass over the text: a reference follows its object wherever the copy holds its Id, even inside a script.
    private static void ReplaceIds(Span<byte> json, Dictionary<Guid, Guid> newIds)
    {
        int i = 0;
        while (i <= json.Length - GuidLength)
        {
            Span<byte> token = json.Slice(i, GuidLength);
            if (token[8] == '-' && token[13] == '-' && token[18] == '-' && token[23] == '-'
                && Utf8Parser.TryParse(token, out Guid id, out int consumed, 'D') && consumed == GuidLength
                && newIds.TryGetValue(id, out Guid newId))
            {
                newId.TryFormat(token, out _);
                i += GuidLength;
            }
            else
            {
                i++;
            }
        }
    }

    // Runs the hook of each object in a copy that must not share a value with its original besides the Ids.
    internal static void RenewCopiedValues(object copy)
    {
        foreach (ICopyRenewable renewable in ChildObjects.Walk(copy, IsReferencedProjectItem).OfType<ICopyRenewable>())
            renewable.RenewForCopy();
    }

    // A project item read from its file, such as a scene a clip draws, is not part of the copy.
    private static bool IsReferencedProjectItem(object obj) => obj is ProjectItem { Uri: not null };

    // The Ids of every object serialized in node, found under the "Id" key that CoreObject writes them to.
    private static IEnumerable<Guid> CollectSerializedIds(JsonNode? node, Dictionary<Guid, HashSet<string>> referenceMembers)
    {
        switch (node)
        {
            case JsonObject obj:
                HashSet<string>? references = null;
                if (obj[nameof(ICoreObject.Id)] is JsonValue ownerValue
                    && ownerValue.TryGetValue(out string? ownerText)
                    && Guid.TryParse(ownerText, out Guid ownerId))
                {
                    referenceMembers.TryGetValue(ownerId, out references);
                }

                foreach ((string key, JsonNode? child) in obj)
                {
                    if (references?.Contains(key) == true)
                        continue;

                    if (key == nameof(ICoreObject.Id)
                        && child is JsonValue value
                        && value.TryGetValue(out string? text)
                        && Guid.TryParse(text, out Guid id)
                        && id != Guid.Empty)
                    {
                        yield return id;
                    }
                    else
                    {
                        foreach (Guid nested in CollectSerializedIds(child, referenceMembers))
                            yield return nested;
                    }
                }

                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    foreach (Guid nested in CollectSerializedIds(item, referenceMembers))
                        yield return nested;
                }

                break;
        }
    }

    private static Span<byte> GetWrittenSpan(PooledArrayBufferWriter<byte> output)
        => PooledArrayBufferWriter<byte>.GetArray(output).AsSpan().Slice(0, output.WrittenCount);
}
