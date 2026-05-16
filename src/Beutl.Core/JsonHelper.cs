using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.JsonConverters;
using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beutl;

public static class JsonHelper
{
    private static readonly ConditionalWeakTable<Type, JsonConverter> s_converters = [];
    private static ILogger? s_logger;

    // Program.Main は GlobalConfiguration.Restore (JsonHelper を経由) を Telemetry が
    // Log.LoggerFactory をセットするより前に呼ぶ。Release ビルドでは LoggerFactory が
    // null のまま CreateLogger を呼ぶと TypeInitializationException で起動が落ちる。
    // 失敗時は NullLogger を返すが、その値はキャッシュしない。これにより
    // LoggerFactory 初期化後の呼び出しでは本物のロガーを取得し直せる。
    private static ILogger Logger
    {
        get
        {
            if (s_logger is not null) return s_logger;
            try
            {
                return s_logger = Log.CreateLogger(typeof(JsonHelper));
            }
            catch
            {
                return NullLogger.Instance;
            }
        }
    }

    public static JsonWriterOptions WriterOptions { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true,
    };

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = null,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals | JsonNumberHandling.AllowReadingFromString,
        Converters =
        {
            new OptionalJsonConverter(),
            new CultureInfoConverter(),
            new DirectoryInfoConverter(),
            new FileInfoConverter(),
            new CoreSerializableJsonConverter(),
            new Vector3JsonConverter(),
            new QuaternionJsonConverter()
            //new CoreObjectJsonConverter()
        }
    };

    public static JsonConverter GetOrCreateConverterInstance(Type converterType)
    {
        if (s_converters.TryGetValue(converterType, out JsonConverter? converter))
        {
            return converter;
        }
        else
        {
            converter = (JsonConverter)Activator.CreateInstance(converterType)!;
            s_converters.Add(converterType, converter);
            return converter;
        }
    }

    public static void JsonSave(this IJsonSerializable serializable, string filename)
    {
        var json = new JsonObject();

        serializable.WriteToJson(json);

        json.JsonSave(filename);
    }

    public static void JsonRestore(this IJsonSerializable serializable, string filename)
    {
        if (JsonRestore(filename) is JsonObject obj)
        {
            serializable.ReadFromJson(obj);
        }
    }

    public static void JsonSave2(this ICoreSerializable serializable, string filename)
    {
        var context = new JsonSerializationContext(serializable.GetType());
        using (ThreadLocalSerializationContext.Enter(context))
        {
            serializable.Serialize(context);

            context.GetJsonObject().JsonSave(filename);
        }
    }

    public static void JsonRestore2(this ICoreSerializable serializable, string filename)
    {
        if (JsonRestore(filename) is JsonObject obj)
        {
            var context = new JsonSerializationContext(
                serializable.GetType(), json: obj);
            using (ThreadLocalSerializationContext.Enter(context))
            {
                serializable.Deserialize(context);
                context.AfterDeserialized(serializable);
            }
        }
    }

    public static void JsonSave(this JsonNode node, string filename)
    {
        // tmp に書いてから rename することで、書き込み中のクラッシュ・電源断・
        // ディスクフルでターゲットファイルがゼロバイトで残るのを防ぐ。
        // 既存ファイルは rename 成功時のみ置き換わるため、ユーザーのプロジェクトや
        // 設定ファイルが破損しない。
        // 固定の `.tmp` サフィックスだとユーザーや他ツールが既に持つ同名ファイルを
        // 上書きしてしまうため、ランダムサフィックスを付与して衝突を避ける。
        string tmp = $"{filename}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new Utf8JsonWriter(stream, WriterOptions))
            {
                node.WriteTo(writer, SerializerOptions);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tmp, filename, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch
            {
                // 失敗しても元の例外は投げる
            }
            throw;
        }
    }

    public static JsonNode? JsonRestore(string filename)
    {
        if (!File.Exists(filename)) return null;
        using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return JsonNode.Parse(stream);
        }
        catch (JsonException ex)
        {
            // 破損ファイルは次回保存時にアトミック書き込みで上書きされるためそのまま放置。
            // ファイルを消すと調査用の証拠を失うので触らない。
            Logger.LogError(ex, "Failed to parse JSON file {Path}; treating as missing.", filename);
            return null;
        }
    }

    public static Type? GetDiscriminator(this JsonNode node)
    {
        return node.TryGetDiscriminator(out Type? type) ? type : null;
    }

    public static Type? GetDiscriminator(this JsonNode node, Type baseType)
    {
        if (node.TryGetDiscriminator(out Type? type))
        {
            return type;
        }
        else
        {
            if (Attribute.GetCustomAttribute(baseType, typeof(FallbackTypeAttribute)) is FallbackTypeAttribute att)
            {
                return att.FallbackType;
            }
            else
            {
                return null;
            }
        }
    }

    public static bool TryGetDiscriminator(this JsonNode node, [NotNullWhen(true)] out Type? type)
    {
        type = null;
        if (node is JsonObject obj)
        {
            JsonNode? typeNode = obj.TryGetPropertyValue("$type", out JsonNode? typeNode1) ? typeNode1
                               : obj.TryGetPropertyValue("@type", out JsonNode? typeNode2) ? typeNode2
                               : null;

            if (typeNode is JsonValue typeValue
                && typeValue.TryGetValue(out string? typeStr)
                && !string.IsNullOrWhiteSpace(typeStr))
            {
                type = TypeFormat.ToType(typeStr);
            }
        }

        return type != null;
    }

    public static bool TryGetDiscriminator(this JsonNode node, [NotNullWhen(true)] out string? result)
    {
        result = null;
        if (node is JsonObject obj)
        {
            JsonNode? typeNode = obj.TryGetPropertyValue("$type", out JsonNode? typeNode1) ? typeNode1
                               : obj.TryGetPropertyValue("@type", out JsonNode? typeNode2) ? typeNode2
                               : null;

            if (typeNode is JsonValue typeValue
                && typeValue.TryGetValue(out string? typeStr)
                && !string.IsNullOrWhiteSpace(typeStr))
            {
                result = typeStr;
            }
        }

        return result != null;
    }

    public static void WriteDiscriminator(this JsonNode obj, Type type)
    {
        obj["$type"] = TypeFormat.ToString(type);
    }

    private static Dictionary<string, object> ParseJson(string json)
    {
        Dictionary<string, JsonElement> dic = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        return dic.ToDictionary(x => x.Key, x => ParseJsonElement(x.Value)!);
    }

    private static object? ParseJsonElement(JsonElement elem)
    {
        return elem.ValueKind switch
        {
            JsonValueKind.String => elem.GetString(),
            JsonValueKind.Number => elem.GetDouble(),
            JsonValueKind.False => false,
            JsonValueKind.True => true,
            JsonValueKind.Array => elem.EnumerateArray().Select(e => ParseJsonElement(e)).ToArray(),
            JsonValueKind.Null => null,
            JsonValueKind.Object => ParseJson(elem.GetRawText()),
            _ => throw new NotSupportedException(),
        };
    }

    public static Dictionary<string, object> ToDictionary(this JsonNode json)
    {
        Dictionary<string, JsonElement> dic = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        return dic.ToDictionary(x => x.Key, x => ParseJsonElement(x.Value)!);
    }

    public static bool TryGetPropertyValueAsJsonValue<T>(this JsonObject obj, string propertyName, [NotNullWhen(true)] out T? value)
    {
        value = default;
        return obj.TryGetPropertyValue(propertyName, out JsonNode? node)
            && node is JsonValue val
            && val.TryGetValue(out value);
    }
}
