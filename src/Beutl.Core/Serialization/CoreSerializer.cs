using System.Text.Json.Nodes;

namespace Beutl.Serialization;

public record CoreSerializerOptions
{
    public Uri? BaseUri { get; init; }

    public CoreSerializationMode? Mode { get; init; }
}

public static class CoreSerializer
{
    public static JsonNode SerializeToJsonNode(object obj, CoreSerializerOptions? options = null)
    {
        var ownerJson = new JsonObject();
        var context = new JsonSerializationContext(
            obj.GetType(), ThreadLocalSerializationContext.Current, ownerJson, options);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            context.SetValue("Value", obj);
        }

        var valueNode = ownerJson["Value"];
        ownerJson.Remove("Value");

        return valueNode!;
    }

    public static JsonObject SerializeToJsonObject(ICoreSerializable obj, CoreSerializerOptions? options = null)
    {
        var type = obj.GetType();
        var context = new JsonSerializationContext(type, ThreadLocalSerializationContext.Current, options: options);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            obj.Serialize(context);
            var jsonObject = context.GetJsonObject();
            jsonObject.WriteDiscriminator(type);
            return jsonObject;
        }
    }

    public static string SerializeToJsonString<T>(T obj, CoreSerializerOptions? options = null)
        where T : ICoreSerializable
    {
        return ConvertToJsonString(SerializeToJsonObject(obj, options));
    }

    public static string SerializeToJsonString(ICoreSerializable obj, CoreSerializerOptions? options = null)
    {
        return ConvertToJsonString(SerializeToJsonObject(obj));
    }

    public static string ConvertToJsonString(JsonObject jsonNode)
    {
        return jsonNode.ToJsonString(JsonHelper.SerializerOptions);
    }

    public static object DeserializeFromJsonObject(JsonObject json, Type baseType, CoreSerializerOptions? options = null)
    {
        Type? actualType = baseType.IsSealed ? baseType : json.GetDiscriminator(baseType);
        if (actualType == null)
        {
            throw new InvalidOperationException("Discriminator not found in JSON object.");
        }

        var obj = Activator.CreateInstance(actualType) as ICoreSerializable
                  ?? throw new InvalidOperationException($"Could not create instance of type {actualType.FullName}.");
        var context = new JsonSerializationContext(actualType, ThreadLocalSerializationContext.Current, json, options);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            obj.Deserialize(context);
            context.AfterDeserialized(obj);
        }

        return obj;
    }

    public static object? DeserializeFromJsonNode(JsonNode json, Type type, CoreSerializerOptions? options = null)
    {
        var ownerJson = new JsonObject { ["Value"] = json.DeepClone() };
        var context = new JsonSerializationContext(type, ThreadLocalSerializationContext.Current, ownerJson, options);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            return context.GetValue("Value", type);
        }
    }

    public static void PopulateFromJsonObject<T>(T obj, JsonObject json, CoreSerializerOptions? options = null)
        where T : ICoreSerializable
    {
        PopulateFromJsonObject(obj, typeof(T), json, options);
    }

    public static void PopulateFromJsonObject(ICoreSerializable obj, Type type, JsonObject json,
        CoreSerializerOptions? options = null)
    {
        var context = new JsonSerializationContext(type, ThreadLocalSerializationContext.Current, json, options);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            obj.Deserialize(context);
            context.AfterDeserialized(obj);
        }
    }
}
