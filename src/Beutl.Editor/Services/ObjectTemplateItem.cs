using System.Text.Json.Nodes;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Editor.Services;

public sealed class ObjectTemplateItem(
    Guid id,
    Type baseType,
    Type actualType,
    JsonObject json,
    string name,
    string categoryFormat,
    string? filePath)
{
    public Guid Id { get; } = id;

    public Type BaseType { get; } = baseType;

    public Type ActualType { get; } = actualType;

    public JsonObject Json { get; } = json;

    public ReactiveProperty<string> Name { get; } = new(name);

    public string CategoryFormat { get; } = categoryFormat;

    public string? FilePath { get; internal set; } = filePath;

    public DateTime LastWriteTimeUtc { get; internal set; }

    // Base64 of 1 MiB, four times what ObjectTemplatePreviewRenderer will ever write — headroom for
    // a package authored elsewhere, without letting one file dictate the allocation.
    private const int MaxEncodedPreviewLength = 1_398_104;

    /// <summary>
    /// A PNG preview of what this template produces, or null when none could be rendered.
    /// </summary>
    public byte[]? Preview { get; internal set; }

    public ICoreSerializable? CreateInstance()
    {
        try
        {
            // A template may reference files (e.g. a material bundled in the same package)
            // with a URI relative to the template file; resolve them against it.
            CoreSerializerOptions? options = FilePath != null
                ? new CoreSerializerOptions { BaseUri = ToFileUri(FilePath) }
                : null;

            return CoreSerializer.DeserializeFromJsonObject(
                (JsonObject)Json.DeepClone(), BaseType, options) as ICoreSerializable;
        }
        catch
        {
            return null;
        }
    }

    // `new Uri(path)` reads a URI-reserved character in the file name — a `#` is legal in a
    // package payload — as syntax, truncating the base path.
    internal static Uri ToFileUri(string path)
    {
        return new UriBuilder("file", string.Empty)
        {
            Path = Path.GetFullPath(path)
        }.Uri;
    }

    public static ObjectTemplateItem CreateFromInstance(ICoreSerializable obj, string name)
    {
        Type actual = obj.GetType();
        (Type baseType, string format) = ObjectTemplateCategoryResolver.Resolve(actual);
        JsonObject json = CoreSerializer.SerializeToJsonObject(obj);

        return new ObjectTemplateItem(Guid.NewGuid(), baseType, actual, json, name, format, null);
    }

    public static JsonNode ToJson(ObjectTemplateItem item)
    {
        var json = new JsonObject
        {
            [nameof(Id)] = item.Id.ToString(),
            [nameof(BaseType)] = TypeFormat.ToString(item.BaseType),
            [nameof(ActualType)] = TypeFormat.ToString(item.ActualType),
            [nameof(Json)] = item.Json.DeepClone(),
            [nameof(CategoryFormat)] = item.CategoryFormat
        };

        if (item.Preview is { Length: > 0 } preview)
        {
            json[nameof(Preview)] = Convert.ToBase64String(preview);
        }

        return json;
    }

    public static ObjectTemplateItem? FromJson(JsonNode json, string name, string filePath, ILogger logger)
    {
        try
        {
            if (json[nameof(Id)]?.GetValue<string>() is not { } idStr
                || !Guid.TryParse(idStr, out Guid id))
            {
                logger.LogError("Invalid or missing Id in template JSON.");
                return null;
            }

            string? baseTypeName = json[nameof(BaseType)]?.ToString();
            if (baseTypeName == null)
            {
                logger.LogError("BaseType is null.");
                return null;
            }

            Type? baseType = TypeFormat.ToType(baseTypeName);
            if (baseType == null)
            {
                logger.LogError("BaseType not found: {TypeName}", baseTypeName);
                return null;
            }

            string? actualTypeName = json[nameof(ActualType)]?.ToString();
            if (actualTypeName == null)
            {
                logger.LogError("ActualType is null.");
                return null;
            }

            Type? actualType = TypeFormat.ToType(actualTypeName);
            if (actualType == null)
            {
                logger.LogError("ActualType not found: {TypeName}", actualTypeName);
                return null;
            }

            if (json[nameof(Json)] is not JsonObject jsonObject)
            {
                logger.LogError("Json object is null.");
                return null;
            }

            string categoryFormat = json[nameof(CategoryFormat)]?.GetValue<string>()
                                    ?? ObjectTemplateCategoryResolver.Resolve(actualType).Format;

            // Detached: a JsonNode holds a strong reference to its parent, so keeping the child
            // would pin the whole parsed document — including the base64 preview — for the
            // lifetime of every item the service caches.
            var payload = (JsonObject)jsonObject.DeepClone();

            return new ObjectTemplateItem(id, baseType, actualType, payload, name, categoryFormat, filePath)
            {
                Preview = ReadPreview(json, logger)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An exception has occurred while creating ObjectTemplateItem from JSON.");
            return null;
        }
    }

    // A corrupt preview must not cost the user the template itself, so it degrades to "no preview".
    // Nothing validates a template file, so the encoded length is untrusted and is bounded before
    // anything is allocated for it.
    private static byte[]? ReadPreview(JsonNode json, ILogger logger)
    {
        if (json[nameof(Preview)] is not JsonValue value
            || !value.TryGetValue(out string? base64)
            || string.IsNullOrEmpty(base64))
        {
            return null;
        }

        if (base64.Length > MaxEncodedPreviewLength)
        {
            logger.LogWarning(
                "Template preview is {Length} characters, over the {Limit} limit; ignoring it.",
                base64.Length, MaxEncodedPreviewLength);
            return null;
        }

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            logger.LogWarning("Template preview is not valid base64; ignoring it.");
            return null;
        }
    }
}
