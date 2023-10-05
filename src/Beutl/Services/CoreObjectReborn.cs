using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Serialization;
using Beutl.Utilities;

namespace Beutl.Services;

public static class CoreObjectReborn
{
    private const int DefaultGuidStringSize = 36;
    private const int BufferSizeDefault = 16 * 1024;

    private static void RebornCore<T>(T obj, PooledArrayBufferWriter<byte> output)
        where T : class, ICoreObject, new()
    {
        var searcher = new ObjectSearcher(obj, v => v is ICoreObject);

        Guid[] ids = searcher.SearchAll()
            .Cast<ICoreObject>()
            .Select(v => v.Id)
            .Distinct()
            .ToArray();

        // JsonObjectに変換
        var jsonObject = new JsonObject();
        var context = new JsonSerializationContext(obj.GetType(), NullSerializationErrorNotifier.Instance, json: jsonObject);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            obj.Serialize(context);
        }

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

    public static void Reborn<T>(T obj, out T newInstance)
        where T : class, ICoreObject, new()
    {
        using var output = new PooledArrayBufferWriter<byte>(BufferSizeDefault);
        RebornCore(obj, output);

        Span<byte> buffer = PooledArrayBufferWriter<byte>.GetArray(output).AsSpan().Slice(0, output.WrittenCount);

        JsonObject jsonObj = JsonNode.Parse(buffer)!.AsObject();
        var instance = new T();

        var context = new JsonSerializationContext(
            typeof(T), NullSerializationErrorNotifier.Instance, json: jsonObj);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            instance.Deserialize(context);
        }

        newInstance = instance;
    }

    public static void Reborn<T>(T obj, out string json)
        where T : class, ICoreObject, new()
    {
        using var output = new PooledArrayBufferWriter<byte>(BufferSizeDefault);
        RebornCore(obj, output);

        Span<byte> buffer = PooledArrayBufferWriter<byte>.GetArray(output).AsSpan().Slice(0, output.WrittenCount);
        json = Encoding.UTF8.GetString(buffer);
    }

    private static void GuidToUtf8(Guid id, Span<byte> utf8)
    {
        Span<char> utf16 = stackalloc char[DefaultGuidStringSize];

        if (!id.TryFormat(utf16, out _))
            throw new Exception("Failed to 'Guid.TryFormat'.");

        Encoding.UTF8.GetBytes(utf16, utf8);
    }
}
