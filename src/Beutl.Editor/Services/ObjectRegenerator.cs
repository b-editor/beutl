using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.NodeGraph.Generative;
using Beutl.Serialization;
using Beutl.Utilities;

namespace Beutl.Editor.Services;

public static class ObjectRegenerator
{
    private const int DefaultGuidStringSize = 36;
    private const int BufferSizeDefault = 16 * 1024;

    private static void RegenerateCore(ICoreSerializable obj, PooledArrayBufferWriter<byte> output)
    {
        var searcher = new ObjectSearcher(obj, v => v is ICoreObject);

        // ICoreObjects with a Uri serialize as a file reference by default, hiding their
        // inner Ids from the JSON. Embed them so GuidToUtf8 can rewrite every Id.
        var jsonObject = CoreSerializer.SerializeToJsonObject(obj, new CoreSerializerOptions
        {
            Mode = CoreSerializationMode.EmbedReferencedObjects
        });

        // A fallback and an object's unknown members keep objects as JSON that no object in the graph
        // stands for, so their Ids are read from the JSON: the copy must not share them with the original.
        Guid[] ids = searcher.SearchAll()
            .Cast<ICoreObject>()
            .Select(v => v.Id)
            .Concat(CollectSerializedIds(jsonObject))
            .Distinct()
            .ToArray();

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

        Span<byte> buffer = GetWrittenSpan(output);
        Span<byte> oldStr = stackalloc byte[DefaultGuidStringSize];
        Span<byte> newStr = stackalloc byte[DefaultGuidStringSize];
        foreach (Guid oldId in ids)
        {
            Guid newId = Guid.NewGuid();
            GuidToUtf8(oldId, oldStr);
            GuidToUtf8(newId, newStr);
            Span<byte> localBuffer = buffer;

            int index;
            while ((index = localBuffer.IndexOf(oldStr)) >= 0)
            {
                newStr.CopyTo(localBuffer.Slice(index));
                localBuffer = localBuffer.Slice(index + newStr.Length);
            }
        }
    }

    // The server answers an idempotency key it has already seen with the earlier job, so an AI node in a copy must
    // not send its first request under the original's key. The key's seed is not an Id, so it is renewed here.
    internal static void RenewRequestKeys(object copy)
    {
        foreach (object node in new ObjectSearcher(copy, static v => v is GenerativeNode).SearchAll())
            ((GenerativeNode)node).RenewRequestKey();
    }

    // The Ids of every object serialized in node, found under the "Id" key that CoreObject writes them to.
    private static IEnumerable<Guid> CollectSerializedIds(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach ((string key, JsonNode? child) in obj)
                {
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
                        foreach (Guid nested in CollectSerializedIds(child))
                            yield return nested;
                    }
                }

                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    foreach (Guid nested in CollectSerializedIds(item))
                        yield return nested;
                }

                break;
        }
    }

    private static Span<byte> GetWrittenSpan(PooledArrayBufferWriter<byte> output)
        => PooledArrayBufferWriter<byte>.GetArray(output).AsSpan().Slice(0, output.WrittenCount);

    private static JsonObject ParseRegeneratedBuffer(Span<byte> buffer)
    {
        return JsonNode.Parse(buffer)!.AsObject();
    }

    public static void Regenerate<T>(T obj, out T newInstance)
        where T : class, ICoreObject, new()
    {
        using var output = new PooledArrayBufferWriter<byte>(BufferSizeDefault);
        RegenerateCore(obj, output);

        Span<byte> buffer = GetWrittenSpan(output);

        JsonObject jsonObj = ParseRegeneratedBuffer(buffer);
        var instance = new T();

        CoreSerializer.PopulateFromJsonObject(instance, jsonObj);
        RenewRequestKeys(instance);

        newInstance = instance;
    }

    // The JSON keeps the request keys of AI nodes; only the overloads that build objects renew them.
    public static void Regenerate(ICoreSerializable obj, out string json)
    {
        using var output = new PooledArrayBufferWriter<byte>(BufferSizeDefault);
        RegenerateCore(obj, output);

        Span<byte> buffer = GetWrittenSpan(output);
        json = Encoding.UTF8.GetString(buffer);
    }

    public static void Regenerate(ICoreSerializable obj, Type actualType, out ICoreSerializable newInstance)
    {
        using var output = new PooledArrayBufferWriter<byte>(BufferSizeDefault);
        RegenerateCore(obj, output);

        Span<byte> buffer = GetWrittenSpan(output);
        JsonObject jsonObj = ParseRegeneratedBuffer(buffer);

        var instance = (ICoreSerializable)Activator.CreateInstance(actualType)!;
        CoreSerializer.PopulateFromJsonObject(instance, actualType, jsonObj);
        RenewRequestKeys(instance);
        newInstance = instance;
    }

    public static void Regenerate<T>(T[] obj, out T[] newInstance)
        where T : class, ICoreObject, new()
    {
        using var output = new PooledArrayBufferWriter<byte>(BufferSizeDefault);
        var wrapper = new ListWrapper<T>();
        wrapper.Items.AddRange(obj);
        RegenerateCore(wrapper, output);

        Span<byte> buffer = GetWrittenSpan(output);

        JsonObject jsonObj = ParseRegeneratedBuffer(buffer);
        var instance = new ListWrapper<T>();

        CoreSerializer.PopulateFromJsonObject(instance, jsonObj);
        RenewRequestKeys(instance);

        newInstance = instance.Items.ToArray();
    }

    private static void GuidToUtf8(Guid id, Span<byte> utf8)
    {
        Span<char> utf16 = stackalloc char[DefaultGuidStringSize];

        id.TryFormat(utf16, out _);
        Encoding.UTF8.GetBytes(utf16, utf8);
    }

    public sealed class ListWrapper<T> : CoreObject
    {
        public static readonly CoreProperty<CoreList<T>> ItemsProperty;
        private readonly CoreList<T> _items = new();

        static ListWrapper()
        {
            ItemsProperty = ConfigureProperty<CoreList<T>, ListWrapper<T>>(nameof(Items))
                .Accessor(o => o.Items, (o, v) => o.Items = v)
                .Register();
        }

        public CoreList<T> Items
        {
            get => _items;
            set => _items.Replace(value);
        }
    }
}
