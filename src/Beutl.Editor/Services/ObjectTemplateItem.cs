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

    public ICoreSerializable? CreateInstance()
    {
        try
        {
            return CoreSerializer.DeserializeFromJsonObject(
                (JsonObject)Json.DeepClone(), BaseType) as ICoreSerializable;
        }
        catch
        {
            return null;
        }
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
        return new JsonObject
        {
            [nameof(Id)] = item.Id.ToString(),
            [nameof(BaseType)] = TypeFormat.ToString(item.BaseType),
            [nameof(ActualType)] = TypeFormat.ToString(item.ActualType),
            [nameof(Json)] = item.Json.DeepClone(),
            [nameof(CategoryFormat)] = item.CategoryFormat
        };
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

            return new ObjectTemplateItem(id, baseType, actualType, jsonObject, name, categoryFormat, filePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An exception has occurred while creating ObjectTemplateItem from JSON.");
            return null;
        }
    }
}
