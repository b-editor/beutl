using System.Text.Json.Nodes;

using Beutl.Utilities;

namespace Beutl.Serialization;

internal static class FallbackDeserializationHelper
{
    internal static ICoreSerializable? TryCreateFallback(
        Type baseType, Type? actualType, JsonObject json, Exception? exception = null)
    {
        if (exception != null && ExceptionHelpers.ContainsFatalFailure(exception)) return null;
        // A referenced file that needs a newer Beutl refuses the whole read rather than leaving a
        // fallback that a save would write over it.
        if (exception != null && ExceptionHelpers.Find<UnsupportedFileVersionException>(exception) is not null) return null;

        if (Attribute.GetCustomAttribute(baseType, typeof(FallbackTypeAttribute))
            is not FallbackTypeAttribute attr)
        {
            return null;
        }

        Type fallbackType = attr.FallbackType;

        // 無限ループ防止: actualTypeが既にfallbackTypeなら再試行しない
        if (actualType == fallbackType)
        {
            return null;
        }

        var fallback = Activator.CreateInstance(fallbackType) as IFallback
            ?? throw new InvalidOperationException(
                $"Could not create fallback instance of type {fallbackType.FullName}.");

        fallback.Json = json.DeepClone().AsObject();
        fallback.Reason = FallbackReason.DeserializationFailed;
        fallback.ErrorMessage = exception?.Message;
        // The fallback stands in for the object the JSON describes, so it takes that object's Id, as one
        // whose type could not be found does when it reads the JSON. Saving writes the JSON back, Id included.
        if (fallback is CoreObject coreObject
            && json[nameof(CoreObject.Id)] is JsonValue idValue
            && idValue.TryGetValue(out string? idText)
            && Guid.TryParse(idText, out Guid id)
            && id != Guid.Empty)
        {
            coreObject.Id.Value = id;
        }

        DeserializationIncidents.RecordFallback(fallback, json);
        return fallback;
    }
}
