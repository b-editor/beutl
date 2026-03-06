using System.Text.Json.Nodes;

namespace Beutl.Serialization;

internal static class FallbackDeserializationHelper
{
    internal static ICoreSerializable? TryCreateFallback(
        Type baseType, Type? actualType, JsonObject json, Exception? exception = null)
    {
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

        return fallback;
    }
}
