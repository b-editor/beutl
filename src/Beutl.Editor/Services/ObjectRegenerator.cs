using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Collections;
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

        Guid[] ids = searcher.SearchAll()
            .Cast<ICoreObject>()
            .Select(v => v.Id)
            .Distinct()
            .ToArray();

        // JsonObjectに変換
        var jsonObject = CoreSerializer.SerializeToJsonObject(obj);

        // UTF-8に書き込む
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

        // Idを置き換える
        Span<byte> buffer = PooledArrayBufferWriter<byte>.GetArray(output).AsSpan().Slice(0, output.WrittenCount);
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
                localBuffer = localBuffer.Slice(index);
                newStr.CopyTo(localBuffer);
            }
        }
    }

    public static void Regenerate<T>(T obj, out T newInstance)
        where T : class, ICoreObject, new()
    {
        using var output = new PooledArrayBufferWriter<byte>(BufferSizeDefault);
        RegenerateCore(obj, output);

        Span<byte> buffer = PooledArrayBufferWriter<byte>.GetArray(output).AsSpan().Slice(0, output.WrittenCount);

        JsonObject jsonObj = JsonNode.Parse(buffer)!.AsObject();
        var instance = new T();

        CoreSerializer.PopulateFromJsonObject(instance, jsonObj);

        newInstance = instance;
    }

    public static void Regenerate(ICoreSerializable obj, out string json)
    {
        using var output = new PooledArrayBufferWriter<byte>(BufferSizeDefault);
        RegenerateCore(obj, output);

        Span<byte> buffer = PooledArrayBufferWriter<byte>.GetArray(output).AsSpan().Slice(0, output.WrittenCount);
        json = Encoding.UTF8.GetString(buffer);
    }

    public static void Regenerate<T>(T[] obj, out T[] newInstance)
        where T : class, ICoreObject, new()
    {
        using var output = new PooledArrayBufferWriter<byte>(BufferSizeDefault);
        var wrapper = new ListWrapper<T>();
        wrapper.Items.AddRange(obj);
        RegenerateCore(wrapper, output);

        Span<byte> buffer = PooledArrayBufferWriter<byte>.GetArray(output).AsSpan().Slice(0, output.WrittenCount);

        JsonObject jsonObj = JsonNode.Parse(buffer)!.AsObject();
        var instance = new ListWrapper<T>();

        CoreSerializer.PopulateFromJsonObject(instance, jsonObj);

        newInstance = instance.Items.ToArray();
    }

    private static void GuidToUtf8(Guid id, Span<byte> utf8)
    {
        Span<char> utf16 = stackalloc char[DefaultGuidStringSize];

        if (!id.TryFormat(utf16, out _))
            throw new Exception("Failed to 'Guid.TryFormat'.");

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
