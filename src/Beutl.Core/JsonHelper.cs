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

namespace Beutl;

public static class JsonHelper
{
    private static readonly ConditionalWeakTable<Type, JsonConverter> s_converters = [];
    private static ILogger? s_logger;

    // Program.Main calls GlobalConfiguration.Restore through JsonHelper before Telemetry
    // configures Log.LoggerFactory. Keep that early NullLogger uncached so later calls can
    // switch to the real application logger.
    private static ILogger Logger => Log.GetLoggerOnceConfigured(ref s_logger, typeof(JsonHelper));

    public static JsonWriterOptions WriterOptions { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true,
        NewLine = "\n",
    };

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        NewLine = "\n",
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

    public static void JsonSave(this JsonNode node, string filename)
        => JsonSave(node, filename, unixFileMode: null);

    internal static void JsonSave(this JsonNode node, string filename, UnixFileMode? unixFileMode)
    {
        WriteAtomically(filename, writer => node.WriteTo(writer, SerializerOptions), journal: false, unixFileMode);
    }

    // Writes to a temporary file and renames it over path, so a failure while writing leaves the existing file whole.
    // The name is random to avoid clobbering an existing file. journal records the write in the active
    // StorageWriteTransaction; unixFileMode applies only to a write that is not journaled.
    internal static void WriteAtomically(
        string path,
        Action<Utf8JsonWriter> write,
        bool journal,
        UnixFileMode? unixFileMode = null,
        bool isCompatibilityGate = false)
    {
        string tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = StorageWriteTransaction.CreateTemporaryFile(
                       tmp, path, privateContents: unixFileMode.HasValue))
            using (var writer = new Utf8JsonWriter(stream, WriterOptions))
            {
                write(writer);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (journal)
            {
                StorageWriteTransaction.MoveIntoPlace(tmp, path, overwrite: true, isCompatibilityGate);
                return;
            }

            if (!OperatingSystem.IsWindows())
            {
                // Read the current permissions at publication, after writing privately.
                UnixFileMode? mode = unixFileMode ?? (File.Exists(path) ? File.GetUnixFileMode(path) : null);
                if (mode is { } permissions)
                    File.SetUnixFileMode(tmp, permissions);
            }
            File.Move(tmp, path, overwrite: true);
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
        type = node.TryGetDiscriminator(out string? typeStr) ? TypeFormat.ToType(typeStr) : null;
        return type != null;
    }

    public static bool TryGetDiscriminator(this JsonNode node, [NotNullWhen(true)] out string? result)
    {
        result = null;
        if (node is JsonObject obj)
        {
            obj.TryGetPropertyValue("$type", out JsonNode? typeNode);

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

    public static bool TryGetPropertyValueAsJsonValue<T>(this JsonObject obj, string propertyName, [NotNullWhen(true)] out T? value)
    {
        value = default;
        return obj.TryGetPropertyValue(propertyName, out JsonNode? node)
            && node is JsonValue val
            && val.TryGetValue(out value);
    }
}
