using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.Editor.Components.Helpers;

public static class FallbackHelper
{
    public static string GetTypeName(object? obj)
    {
        if ((obj as IFallback)?.TryGetTypeName(out string? result) == true)
        {
            return result;
        }
        else if (obj != null)
        {
            return TypeFormat.ToString(obj.GetType());
        }
        else
        {
            return Strings.Unknown;
        }
    }

    public static string GetFallbackMessage(object? obj)
    {
        if (obj is IFallback { Reason: FallbackReason.DeserializationFailed } fallback)
        {
            return fallback.ErrorMessage != null
                ? $"{MessageStrings.RestoreFailedDeserializationError}\n{fallback.ErrorMessage}"
                : MessageStrings.RestoreFailedDeserializationError;
        }

        return MessageStrings.RestoreFailedTypeNotFound;
    }

    public static IObservable<string?> GetFallbackJson<T>(IObservable<T?> value)
        where T : class
    {
        return value.Select(v =>
        {
            if (v is IFallback { Json: JsonObject json })
            {
                return json.ToJsonString(JsonHelper.SerializerOptions);
            }

            return null;
        });
    }

    public static T DeserializeInstance<T>(string? str)
        where T : class, ICoreSerializable
    {
        string message = MessageStrings.InvalidJson;
        _ = str ?? throw new Exception(message);
        JsonObject json = (JsonNode.Parse(str) as JsonObject) ?? throw new Exception(message);

        Type? type = json.GetDiscriminator();
        T? instance = null;
        if (type?.IsAssignableTo(typeof(T)) ?? false)
        {
            instance = Activator.CreateInstance(type) as T;
        }

        if (instance == null) throw new Exception(message);

        CoreSerializer.PopulateFromJsonObject(instance, type!, json);
        return instance;
    }
}
