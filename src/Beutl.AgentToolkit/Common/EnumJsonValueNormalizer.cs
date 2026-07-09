using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Common;

internal static class EnumJsonValueNormalizer
{
    public static object? Deserialize(JsonNode node, Type targetType, CoreSerializerOptions? options = null)
    {
        return CoreSerializer.DeserializeFromJsonNode(Normalize(node, targetType), targetType, options);
    }

    public static JsonNode Normalize(JsonNode node, Type targetType)
    {
        Type? enumType = GetEnumType(targetType);
        if (enumType is not null
            && node is JsonValue value
            && value.TryGetValue(out string? text))
        {
            object enumValue = ParseEnumString(enumType, text);
            object numericValue = Convert.ChangeType(enumValue, Enum.GetUnderlyingType(enumType), CultureInfo.InvariantCulture);
            return JsonValue.Create(numericValue)!;
        }

        if (node is JsonObject obj && typeof(ICoreSerializable).IsAssignableFrom(targetType))
        {
            return NormalizeCoreSerializable(obj, targetType);
        }

        return node.DeepClone();
    }

    public static IReadOnlyList<string>? GetEnumNames(Type targetType)
    {
        Type? enumType = GetEnumType(targetType);
        return enumType is null ? null : Enum.GetNames(enumType);
    }

    private static Type? GetEnumType(Type targetType)
    {
        Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return type.IsEnum ? type : null;
    }

    private static JsonObject NormalizeCoreSerializable(JsonObject obj, Type targetType)
    {
        JsonObject normalized = (JsonObject)obj.DeepClone();
        Type? actualType = targetType.IsSealed ? targetType : normalized.GetDiscriminator(targetType);
        if (actualType is null)
        {
            return normalized;
        }

        foreach (CoreProperty property in PropertyRegistry.GetRegistered(actualType))
        {
            NormalizeObjectProperty(normalized, property.Name, property.PropertyType);
        }

        if (typeof(EngineObject).IsAssignableFrom(actualType)
            && Activator.CreateInstance(actualType) is EngineObject engineObject)
        {
            foreach (IProperty property in engineObject.Properties)
            {
                NormalizeObjectProperty(normalized, property.Name, property.ValueType);
                if (property is IListProperty listProperty)
                {
                    NormalizeListProperty(normalized, property.Name, listProperty.ElementType);
                }
            }
        }

        return normalized;
    }

    private static void NormalizeObjectProperty(JsonObject obj, string name, Type valueType)
    {
        if (obj.TryGetPropertyValue(name, out JsonNode? valueNode) && valueNode is not null)
        {
            obj[name] = Normalize(valueNode, valueType);
        }
    }

    private static void NormalizeListProperty(JsonObject obj, string name, Type elementType)
    {
        if (!obj.TryGetPropertyValue(name, out JsonNode? valueNode) || valueNode is not JsonArray array)
        {
            return;
        }

        var normalizedArray = new JsonArray();
        foreach (JsonNode? item in array)
        {
            normalizedArray.Add(item is null ? null : Normalize(item, elementType));
        }

        obj[name] = normalizedArray;
    }

    private static object ParseEnumString(Type enumType, string text)
    {
        string trimmed = text.Trim();
        string? name = Enum.GetNames(enumType)
            .FirstOrDefault(value => string.Equals(value, trimmed, StringComparison.OrdinalIgnoreCase));
        if (name is not null)
        {
            return Enum.Parse(enumType, name);
        }

        throw new JsonException(
            $"Value '{text}' is not a valid {enumType.FullName} enum name.");
    }
}
