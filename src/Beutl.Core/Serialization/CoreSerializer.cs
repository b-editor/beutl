using System.Text.Json;
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

    public static T RestoreFromUri<T>(Uri uri)
        where T : ICoreSerializable
    {
        return (T)RestoreFromUri(uri, typeof(T));
    }

    public static object RestoreFromUri(Uri uri, Type type)
    {
        using var stream = UriHelper.ResolveStream(uri);

        var node = JsonNode.Parse(stream);
        if (node is not JsonObject jsonObject) throw new JsonException();

        // 互換性処理
        if (type == typeof(ProjectItem) && !node.TryGetDiscriminator(out Type _))
        {
            node["$type"] = "[Beutl.ProjectSystem]:Scene";
        }

        Type? actualType = type.IsSealed ? type : jsonObject.GetDiscriminator(type);
        if (actualType == null)
        {
            throw new InvalidOperationException("Discriminator not found in JSON object.");
        }

        var obj = Activator.CreateInstance(actualType) as ICoreSerializable
                  ?? throw new InvalidOperationException($"Could not create instance of type {actualType.FullName}.");

        if (obj is CoreObject coreObj)
        {
            coreObj.Uri = uri;
        }

        PopulateFromJsonObject(
            obj, type, jsonObject, new CoreSerializerOptions { BaseUri = uri });

        return obj;
    }

    public static void PopulateFromUri<T>(T obj, Uri uri)
        where T : ICoreSerializable
    {
        PopulateFromUri(obj, typeof(T), uri);
    }

    public static void PopulateFromUri(ICoreSerializable obj, Type type, Uri uri)
    {
        using var stream = UriHelper.ResolveStream(uri);

        var node = JsonNode.Parse(stream);
        if (node is not JsonObject jsonObject) throw new JsonException();
        if (obj is CoreObject coreObj)
        {
            coreObj.Uri = uri;
        }

        PopulateFromJsonObject(
            obj, type, jsonObject, new CoreSerializerOptions { BaseUri = uri });
    }

    public static void StoreToUri<T>(T obj, Uri uri)
        where T : ICoreSerializable
    {
        if (uri.Scheme == "file")
        {
            if (obj is CoreObject coreObj)
            {
                coreObj.Uri = uri;
            }

            var directory = Path.GetDirectoryName(uri.LocalPath);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(uri.LocalPath, FileMode.Create, FileAccess.Write, FileShare.Write);
            using var writer = new Utf8JsonWriter(stream, JsonHelper.WriterOptions);

            SerializeToJsonObject(obj, new CoreSerializerOptions { BaseUri = uri })
                .WriteTo(writer, JsonHelper.SerializerOptions);
        }
        else
        {
            throw new JsonException();
        }
    }
}
