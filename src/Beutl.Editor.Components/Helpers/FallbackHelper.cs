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
                ? $"{Message.Could_not_restore_because_an_exception_occurred}\n{fallback.ErrorMessage}"
                : Message.Could_not_restore_because_an_exception_occurred;
        }

        return Message.Could_not_restore_because_type_could_not_be_found;
    }
}
