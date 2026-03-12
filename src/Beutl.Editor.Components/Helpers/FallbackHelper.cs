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
                ? $"{Message.RestoreFailedDeserializationError}\n{fallback.ErrorMessage}"
                : Message.RestoreFailedDeserializationError;
        }

        return Message.RestoreFailedTypeNotFound;
    }
}
