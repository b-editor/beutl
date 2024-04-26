using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.Helpers;

public static class CoreSerializerHelper
{
    public static JsonObject SerializeToJsonObject<T>(T obj)
        where T : ICoreSerializable
    {
        return SerializeToJsonObject(obj, typeof(T));
    }

    public static JsonObject SerializeToJsonObject(ICoreSerializable obj, Type type)
    {
        var context = new JsonSerializationContext(type, NullSerializationErrorNotifier.Instance);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            obj.Serialize(context);
            return context.GetJsonObject();
        }
    }

    public static string SerializeToJsonString<T>(T obj)
        where T : ICoreSerializable
    {
        return ConvertToJsonString(SerializeToJsonObject(obj));
    }

    public static string SerializeToJsonString(ICoreSerializable obj, Type type)
    {
        return ConvertToJsonString(SerializeToJsonObject(obj, type));
    }

    public static string ConvertToJsonString(JsonObject jsonNode)
    {
        return jsonNode.ToJsonString(JsonHelper.SerializerOptions);
    }

    public static void PopulateFromJsonObject<T>(T obj, JsonObject json)
        where T : ICoreSerializable
    {
        PopulateFromJsonObject(obj, typeof(T), json);
    }

    public static void PopulateFromJsonObject(ICoreSerializable obj, Type type, JsonObject json)
    {
        var context = new JsonSerializationContext(type, NullSerializationErrorNotifier.Instance, null, json);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            obj.Deserialize(context);
        }
    }
}
