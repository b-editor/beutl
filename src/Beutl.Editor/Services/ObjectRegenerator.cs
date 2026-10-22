using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Serialization;
using Beutl.Utilities;

namespace Beutl.Editor.Services;

public static class ObjectRegenerator
{
    private const int GuidLength = 36;
    private const int BufferSizeDefault = 16 * 1024;

    private static void RegenerateCore(ICoreSerializable obj, PooledArrayBufferWriter<byte> output)
    {
        // ICoreObjects with a Uri serialize as a file reference by default, hiding their
        // inner Ids from the JSON. Embed them so every Id of the copy is in it.
        var jsonObject = CoreSerializer.SerializeToJsonObject(obj, new CoreSerializerOptions
        {
            Mode = CoreSerializationMode.EmbedReferencedObjects
        });

        // Each object of the copy writes its Id, including fallbacks and the objects unknown members keep.
        var newIds = new Dictionary<Guid, Guid>();
        foreach (Guid id in CollectSerializedIds(jsonObject))
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
        foreach (ICopyRenewable renewable in ChildObjects.Walk(copy).OfType<ICopyRenewable>())
            renewable.RenewForCopy();
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
        RenewCopiedValues(instance);

        newInstance = instance;
    }

    // The JSON keeps what copies renew, such as AI nodes' request keys; only the overloads that build objects renew it.
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
        RenewCopiedValues(instance);
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
        RenewCopiedValues(instance);

        newInstance = instance.Items.ToArray();
    }

    public sealed class ListWrapper<T> : CoreObject
    {
        private readonly CoreList<T> _items = new();

        public ListWrapper()
        {
            // The list is a value rather than a default, as its CoreProperty form had no default.
            ItemsProperty.Value = _items;
            ScanProperties<ListWrapper<T>>();
        }

        public IProperty<CoreList<T>> ItemsProperty { get; } = new ValueOnlyProperty<CoreList<T>>(null!) { KeepsList = true };

        public CoreList<T> Items
        {
            get => _items;
            set => _items.Replace(value);
        }

        // Items keeps its list, as its CoreProperty accessor did.
        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            KeptListReader.ReadInto<CoreList<T>, T>(context, nameof(Items), _items);
        }
    }
}
