using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.Animation.Easings;

namespace Beutl.Converters;

internal sealed class EasingJsonConverter : JsonConverter<Easing>
{
    public override Easing Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        JsonNode? node = JsonNode.Parse(ref reader);
        if (node is JsonValue typeValue && typeValue.TryGetValue(out string? typeName))
        {
            Type type = ResolveEasingType(TypeFormat.ToType(typeName));
            if (type.GetConstructor(Type.EmptyTypes) is null)
                throw new JsonException($"The easing type '{typeName}' does not have a public parameterless constructor.");

            return (Easing)Activator.CreateInstance(type)!;
        }

        if (node is not JsonObject obj)
            throw new JsonException("An easing must be a type name or an object.");

        // Older spline values contain only control points. An explicit discriminator must
        // resolve successfully so an unavailable plugin is not silently saved as a spline.
        bool hasDiscriminator = obj.ContainsKey("$type") || obj.ContainsKey("@type");
        Type actualType = ResolveEasingType(hasDiscriminator ? obj.GetDiscriminator() : typeof(SplineEasing));
        if (actualType == typeof(SplineEasing))
        {
            float x1 = ReadSplinePoint(obj, "X1", 0);
            float y1 = ReadSplinePoint(obj, "Y1", 0);
            float x2 = ReadSplinePoint(obj, "X2", 1);
            float y2 = ReadSplinePoint(obj, "Y2", 1);
            if (x1 is < 0 or > 1 || x2 is < 0 or > 1)
                throw new JsonException("Spline easing X control points must be between 0 and 1.");

            return new SplineEasing(x1, y1, x2, y2);
        }

        return obj.Deserialize(actualType, options) as Easing
               ?? throw new JsonException($"Could not deserialize the easing type '{actualType}'.");
    }

    public override void Write(Utf8JsonWriter writer, Easing value, JsonSerializerOptions options)
    {
        // Serialize the runtime type to retain derived properties rather than the empty
        // abstract Easing contract, then identify that type for the corresponding read.
        JsonObject obj = JsonSerializer.SerializeToNode(value, value.GetType(), options) as JsonObject
                         ?? throw new JsonException("An easing must serialize to an object.");
        obj.WriteDiscriminator(value.GetType());
        obj.WriteTo(writer, options);
    }

    private static Type ResolveEasingType(Type? type)
    {
        if (type is null || !typeof(Easing).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters)
            throw new JsonException("The easing type could not be resolved to a concrete Easing.");

        return type;
    }

    private static float ReadSplinePoint(JsonObject obj, string name, float fallback)
    {
        if (!obj.TryGetPropertyValue(name, out JsonNode? node)) return fallback;
        if (node is JsonValue number && number.TryGetValue(out float value) && float.IsFinite(value))
            return value;

        throw new JsonException($"Spline easing control point '{name}' must be a finite number.");
    }
}
