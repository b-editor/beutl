using System.Text.Json.Nodes;

namespace Beutl.Serialization;

internal static class FallbackDeserializationHelper
{
    /// <summary>
    /// Creates a fallback instance for a deserialization failure when the base type specifies one.
    /// </summary>
    /// <param name="baseType">The type whose fallback configuration is used.</param>
    /// <param name="actualType">The type being deserialized, if known.</param>
    /// <param name="json">The JSON object to associate with the fallback instance.</param>
    /// <param name="exception">The exception that caused deserialization to fail, if available.</param>
    /// <returns>The configured fallback instance, or <c>null</c> when no fallback applies.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the configured fallback type cannot be created as an <see cref="IFallback"/>.</exception>
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

        DeserializationIncidents.RecordFallback();
        return fallback;
    }
}
